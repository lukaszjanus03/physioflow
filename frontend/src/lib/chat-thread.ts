import { chatApi, chatAccessLost, type ChatConversation, type ChatMessage } from './chat-api';
import { continuousChatSequence, mergeChatMessages, validChatCursor } from './chat-messages';

export type ThreadSnapshot = {
  conversation: ChatConversation | null;
  messages: ChatMessage[];
  loading: boolean;
  busy: boolean;
  error: unknown;
  revoked: boolean;
  hasOlder: boolean;
  revision: number;
  change: 'initial' | 'sync' | 'older';
};

export class ChatThread {
  private state: ThreadSnapshot = {
    conversation: null,
    messages: [],
    loading: true,
    busy: false,
    error: null,
    revoked: false,
    hasOlder: false,
    revision: 0,
    change: 'initial',
  };
  private listeners = new Set<() => void>();
  private requests = new Set<AbortController>();
  private disposed = false;
  private running = false;
  private again = false;
  private waiters = new Set<() => void>();
  private receiptsDirty = false;
  private floor = 0;
  private cursor = 0;
  private older: number | null = null;
  private reading = false;
  private nextRead: ChatMessage | null = null;
  private readBlocked = false;
  constructor(
    readonly id: string,
    readonly owner: string,
    private callbacks: { changed: () => void; denied: (id: string) => void },
  ) {}
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };
  snapshot = () => this.state;
  private finish() {
    this.running = false;
    this.update({ busy: false });
    if (this.again && !this.disposed && !this.state.revoked) {
      this.again = false;
      void this.refresh();
    } else {
      for (const resolve of this.waiters) resolve();
      this.waiters.clear();
    }
  }
  private update(value: Partial<ThreadSnapshot>) {
    if (this.disposed) return;
    this.state = { ...this.state, ...value, revision: this.state.revision + 1 };
    for (const listener of this.listeners) listener();
  }
  private async request<T>(run: (signal: AbortSignal) => Promise<T>): Promise<T> {
    const controller = new AbortController();
    this.requests.add(controller);
    if (this.disposed || this.state.revoked) controller.abort();
    try {
      return await run(controller.signal);
    } finally {
      this.requests.delete(controller);
    }
  }
  stop() {
    this.disposed = true;
    for (const controller of this.requests) controller.abort();
    this.requests.clear();
    this.listeners.clear();
    this.state = { ...this.state, conversation: null, messages: [], error: null };
    for (const resolve of this.waiters) resolve();
    this.waiters.clear();
  }
  revoke() {
    if (this.state.revoked || this.disposed) return;
    this.update({
      conversation: null,
      messages: [],
      revoked: true,
      loading: false,
      busy: false,
      error: { isAxiosError: true, response: { status: 404 } },
    });
    for (const controller of this.requests) controller.abort();
  }
  private fail(error: unknown) {
    if (this.disposed || this.state.revoked) return;
    if (chatAccessLost(error)) {
      this.revoke();
      this.callbacks.denied(this.id);
    } else this.update({ error, loading: false });
  }
  private merge(incoming: ChatMessage[], change: ThreadSnapshot['change']) {
    const scoped = incoming.filter((message) => message.conversationId === this.id);
    const messages = mergeChatMessages(this.state.messages, scoped);
    // A confirmed send can arrive ahead of missing messages; never skip that gap.
    this.cursor = continuousChatSequence(messages, this.floor);
    this.update({ messages, change });
  }
  async start() {
    if (this.running || this.disposed || this.state.revoked) return;
    this.running = true;
    try {
      const [conversation, history] = await Promise.all([
        this.request((signal) => chatApi.conversation(this.id, signal)),
        this.request((signal) => chatApi.history(this.id, {}, signal)),
      ]);
      if (this.disposed || this.state.revoked) return;
      this.floor = history.items[0]?.sequence ? history.items[0].sequence - 1 : 0;
      this.older = history.nextCursor;
      this.update({ conversation, hasOlder: history.hasMore, loading: false, error: null });
      this.merge(history.items, 'initial');
      await this.syncNew();
    } catch (error) {
      this.fail(error);
    } finally {
      this.finish();
    }
  }
  private async syncNew() {
    while (!this.disposed && !this.state.revoked) {
      const after = this.cursor;
      const history = await this.request((signal) => chatApi.history(this.id, { after }, signal));
      if (this.disposed || this.state.revoked) return;
      this.merge(history.items, 'sync');
      if (!history.hasMore) return;
      if (
        !validChatCursor(history.nextCursor) ||
        history.nextCursor <= after ||
        this.cursor < history.nextCursor
      )
        throw new Error('chat-history-gap');
    }
  }
  private async refreshReceipts() {
    const oldest = this.state.messages[0]?.sequence;
    const newest = this.state.messages.at(-1)?.sequence;
    if (!oldest || !newest) return;
    let before = newest + 1;
    while (!this.disposed && !this.state.revoked) {
      const history = await this.request((signal) => chatApi.history(this.id, { before }, signal));
      if (this.disposed || this.state.revoked) return;
      this.merge(history.items, 'sync');
      if (!history.hasMore || (history.items[0]?.sequence ?? oldest) <= oldest) return;
      if (!validChatCursor(history.nextCursor) || history.nextCursor >= before)
        throw new Error('chat-history-cursor');
      before = history.nextCursor;
    }
  }
  async refresh() {
    if (this.disposed || this.state.revoked) return;
    if (this.running) {
      this.again = true;
      return new Promise<void>((resolve) => {
        this.waiters.add(resolve);
      });
    }
    if (!this.state.conversation) return this.start();
    this.running = true;
    this.readBlocked = false;
    this.update({ busy: true });
    try {
      const conversation = await this.request((signal) => chatApi.conversation(this.id, signal));
      if (this.disposed || this.state.revoked) return;
      const changed = conversation.version !== this.state.conversation?.version;
      this.receiptsDirty ||= changed;
      this.update({ conversation, error: null });
      await this.syncNew();
      if (this.receiptsDirty) {
        await this.refreshReceipts();
        this.receiptsDirty = false;
      }
    } catch (error) {
      this.fail(error);
    } finally {
      this.finish();
    }
  }
  async loadOlder() {
    if (this.running || this.disposed || this.state.revoked || !this.state.hasOlder || this.older === null)
      return;
    this.running = true;
    this.update({ busy: true });
    try {
      const history = await this.request((signal) =>
        chatApi.history(this.id, { before: this.older! }, signal),
      );
      if (this.disposed || this.state.revoked) return;
      this.older = history.nextCursor;
      this.update({ hasOlder: history.hasMore, error: null });
      this.merge(history.items, 'older');
    } catch (error) {
      this.fail(error);
    } finally {
      this.finish();
    }
  }
  accept(message: ChatMessage) {
    if (this.disposed || this.state.revoked || message.conversationId !== this.id) return;
    this.merge([message], 'sync');
    this.callbacks.changed();
    void this.refresh();
  }
  async markVisible(message: ChatMessage) {
    if (
      !this.state.messages.some(
        (item) =>
          item.id === message.id && item.recipientId === this.owner && item.sequence === message.sequence,
      )
    )
      return;
    if (
      this.disposed ||
      this.state.revoked ||
      this.readBlocked ||
      message.recipientId !== this.owner ||
      message.sequence <= (this.state.conversation?.readThrough ?? 0)
    )
      return;
    if (this.reading) {
      if (!this.nextRead || message.sequence > this.nextRead.sequence) this.nextRead = message;
      return;
    }
    this.reading = true;
    try {
      const result = await this.request((signal) => chatApi.read(this.id, message.id, signal));
      if (this.disposed || this.state.revoked || !this.state.conversation) return;
      this.update({
        conversation: {
          ...this.state.conversation,
          readThrough: Math.max(this.state.conversation.readThrough, result.readThrough),
          unreadCount: result.unreadCount,
        },
      });
      this.callbacks.changed();
      void this.refresh();
    } catch (error) {
      this.readBlocked = true;
      this.fail(error);
    } finally {
      this.reading = false;
      const next = this.nextRead;
      this.nextRead = null;
      if (next) void this.markVisible(next);
    }
  }
}
