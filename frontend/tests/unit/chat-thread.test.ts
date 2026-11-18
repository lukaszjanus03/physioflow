import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { chatApi, type ChatHistory } from '@/lib/chat-api';
import { ChatThread } from '@/lib/chat-thread';
import { conversation, chatId, message } from '../fixtures/chat';
import { ids } from '../fixtures/sharing';

const history = (
  items: ReturnType<typeof message>[] = [],
  hasMore = false,
  nextCursor: number | null = null,
): ChatHistory => ({ items, hasMore, nextCursor, lastSequence: 999 });
let thread: ChatThread;
const changed = vi.fn();
const denied = vi.fn();
beforeEach(() => {
  vi.spyOn(chatApi, 'conversation').mockResolvedValue({ ...conversation });
  vi.spyOn(chatApi, 'history').mockResolvedValue(history());
  thread = new ChatThread(chatId, ids.patient, { changed, denied });
});
afterEach(() => thread.stop());

describe('chat thread synchronization', () => {
  it('aborts outstanding requests and ignores their late results after stop', async () => {
    let signal: AbortSignal | undefined;
    vi.mocked(chatApi.history).mockImplementation((_id, _cursor, current) => {
      signal = current;
      return new Promise((_resolve, reject) =>
        current.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))),
      );
    });
    const initial = thread.start();
    thread.stop();
    await initial;
    expect(signal?.aborted).toBe(true);
    expect(thread.snapshot().messages).toEqual([]);
  });
  it('fetches all missing pages from actually loaded sequence, not server lastSequence', async () => {
    vi.mocked(chatApi.history)
      .mockResolvedValueOnce(history([message(151), message(152)], true, 151))
      .mockResolvedValueOnce(history([message(153)], true, 153))
      .mockResolvedValueOnce(history([message(154)]));
    await thread.start();
    expect(vi.mocked(chatApi.history).mock.calls.map((args) => args[1])).toEqual([
      {},
      { after: 152 },
      { after: 153 },
    ]);
    expect(thread.snapshot().messages.map((m) => m.sequence)).toEqual([151, 152, 153, 154]);
  });
  it('does not skip gaps when an own send is confirmed out of order', async () => {
    vi.mocked(chatApi.history)
      .mockResolvedValueOnce(history([message(1)]))
      .mockResolvedValue(history());
    await thread.start();
    thread.accept(message(5, { senderId: ids.patient, recipientId: ids.therapist }));
    await thread.refresh();
    expect(
      vi
        .mocked(chatApi.history)
        .mock.calls.slice(2)
        .every((args) => args[1].after === 1),
    ).toBe(true);
    expect(thread.snapshot().messages.map((m) => m.sequence)).toEqual([1, 5]);
  });
  it('deduplicates history and confirmed send', async () => {
    vi.mocked(chatApi.history).mockResolvedValue(history([message(1)]));
    await thread.start();
    thread.accept(message(1));
    await thread.refresh();
    expect(thread.snapshot().messages).toHaveLength(1);
  });
  it('pages older messages without moving the forward cursor backwards', async () => {
    vi.mocked(chatApi.history)
      .mockResolvedValueOnce(history([message(3)], true, 3))
      .mockResolvedValueOnce(history())
      .mockResolvedValueOnce(history([message(1), message(2)]))
      .mockResolvedValue(history());
    await thread.start();
    await thread.loadOlder();
    await thread.refresh();
    expect(thread.snapshot().messages.map((m) => m.sequence)).toEqual([1, 2, 3]);
    expect(vi.mocked(chatApi.history).mock.calls.at(-1)?.[1]).toEqual({ after: 3 });
  });
  it('rejects a non-progressing synchronization cursor instead of looping', async () => {
    vi.mocked(chatApi.history)
      .mockResolvedValueOnce(history())
      .mockResolvedValue(history([], true, 0));
    await thread.start();
    expect(thread.snapshot().error).toBeInstanceOf(Error);
    expect(chatApi.history).toHaveBeenCalledTimes(2);
  });
  it('retries failed receipt loading even after the version was already updated', async () => {
    vi.mocked(chatApi.history)
      .mockResolvedValueOnce(history([message(1)]))
      .mockResolvedValue(history());
    await thread.start();
    vi.mocked(chatApi.conversation).mockResolvedValue({ ...conversation, version: 'v2' });
    let receiptCalls = 0;
    vi.mocked(chatApi.history).mockImplementation(async (_id, cursor) => {
      if (cursor.before) {
        if (++receiptCalls === 1) throw new Error('offline');
        return history([message(1, { readAt: '2026-11-18T14:00:00Z' })]);
      }
      return history();
    });
    await thread.refresh();
    await thread.refresh();
    expect(receiptCalls).toBe(2);
    expect(thread.snapshot().messages[0].readAt).not.toBeNull();
  });
  it('waits for queued refresh callers and stops safely', async () => {
    await thread.start();
    let release!: (value: typeof conversation) => void;
    vi.mocked(chatApi.conversation).mockReturnValueOnce(
      new Promise((resolve) => {
        release = resolve;
      }),
    );
    const first = thread.refresh();
    let finished = false;
    const second = thread.refresh().then(() => {
      finished = true;
    });
    await Promise.resolve();
    expect(finished).toBe(false);
    release(conversation);
    await first;
    await second;
    expect(finished).toBe(true);
  });
  it.each([403, 404])('clears previously displayed data after access loss %i', async (status) => {
    vi.mocked(chatApi.history).mockResolvedValue(history([message(1)]));
    await thread.start();
    vi.mocked(chatApi.conversation).mockRejectedValue({ isAxiosError: true, response: { status } });
    await thread.refresh();
    expect(thread.snapshot()).toMatchObject({ messages: [], conversation: null, revoked: true });
    expect(denied).toHaveBeenCalledWith(chatId);
  });
  it('retains visible history for a temporary network failure', async () => {
    vi.mocked(chatApi.history).mockResolvedValue(history([message(1)]));
    await thread.start();
    vi.mocked(chatApi.conversation).mockRejectedValue(new Error('offline'));
    await thread.refresh();
    expect(thread.snapshot().messages).toHaveLength(1);
    expect(thread.snapshot().revoked).toBe(false);
  });
  it('marks only known incoming messages and uses the server read cursor', async () => {
    const read = vi
      .spyOn(chatApi, 'read')
      .mockResolvedValue({ conversationId: chatId, readerId: ids.patient, readThrough: 1, unreadCount: 4 });
    vi.mocked(chatApi.history).mockResolvedValue(
      history([message(1), message(2, { senderId: ids.patient, recipientId: ids.therapist })]),
    );
    await thread.start();
    await thread.markVisible(message(999));
    await thread.markVisible(message(2, { senderId: ids.patient, recipientId: ids.therapist }));
    expect(read).not.toHaveBeenCalled();
    await thread.markVisible(message(1));
    expect(read.mock.calls[0].slice(0, 2)).toEqual([chatId, message(1).id]);
    expect(changed).toHaveBeenCalled();
  });
  it('clears memory and aborts in-flight requests on disposal', async () => {
    let signal: AbortSignal | undefined;
    vi.mocked(chatApi.history).mockImplementation(async (_id, _cursor, current) => {
      signal = current;
      return history([message(1)]);
    });
    await thread.start();
    thread.stop();
    expect(thread.snapshot().messages).toEqual([]);
    expect(signal).toBeDefined();
    const calls = vi.mocked(chatApi.history).mock.calls.length;
    await thread.refresh();
    expect(chatApi.history).toHaveBeenCalledTimes(calls);
  });
});
