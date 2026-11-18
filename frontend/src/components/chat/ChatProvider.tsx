'use client';

import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useSignalR, useSignalREvent } from '@/lib/signalr-context';
import { useAuthStore } from '@/stores/auth-store';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { allChatConversations, chatAccessLost, type ChatConversation, type ChatHint } from '@/lib/chat-api';

type Guard = { dirty: boolean; busy: boolean };
type ChatContextValue = {
  owner: string | null;
  conversations: ChatConversation[];
  loading: boolean;
  fetching: boolean;
  error: unknown;
  connected: boolean;
  blocked: Set<string>;
  refresh: () => Promise<void>;
  retryAll: () => void;
  deny: (id: string) => void;
  allow: (id: string) => void;
  subscribe: (listener: (hint: ChatHint) => void) => () => void;
  setGuard: (value: Guard) => void;
  confirmLeave: (run: () => void) => void;
};
const defaults: ChatContextValue = {
  owner: null,
  conversations: [],
  loading: false,
  fetching: false,
  error: null,
  connected: false,
  blocked: new Set(),
  refresh: async () => {},
  retryAll: () => {},
  deny: () => {},
  allow: () => {},
  subscribe: () => () => {},
  setGuard: () => {},
  confirmLeave: (run) => run(),
};
const ChatContext = createContext<ChatContextValue>(defaults);
export const useChat = () => useContext(ChatContext);

export function ChatProvider({ children }: { children: ReactNode }) {
  const { userId, roles, isAuthenticated } = useAuthStore();
  const owner = isAuthenticated && (roles.includes('Patient') || roles.includes('Therapist')) ? userId : null;
  return (
    <ChatAccount key={owner ?? 'guest'} owner={owner}>
      {children}
    </ChatAccount>
  );
}

function ChatAccount({ owner, children }: { owner: string | null; children: ReactNode }) {
  const { isConnected } = useSignalR();
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [loading, setLoading] = useState(Boolean(owner));
  const [fetching, setFetching] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [blocked, setBlocked] = useState<Set<string>>(() => new Set());
  const [guard, setGuard] = useState<Guard>({ dirty: false, busy: false });
  const navigation = useUnsavedChanges(guard.dirty, guard.busy);
  const state = useRef({
    alive: true,
    request: null as AbortController | null,
    again: false,
    denied: false,
    blocked: new Set<string>(),
  });
  const [listeners] = useState(() => new Set<(hint: ChatHint) => void>());
  const emit = useCallback(
    (hint: ChatHint) => {
      for (const listener of listeners) listener(hint);
    },
    [listeners],
  );
  const subscribe = useCallback(
    (listener: (hint: ChatHint) => void) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    [listeners],
  );

  const refresh = useCallback(
    async function refreshList() {
      if (!owner || !state.current.alive || state.current.denied) return;
      if (state.current.request) {
        state.current.again = true;
        return;
      }
      const controller = new AbortController();
      state.current.request = controller;
      setFetching(true);
      try {
        const items = await allChatConversations(controller.signal);
        if (!controller.signal.aborted && state.current.alive) {
          setConversations(items.filter((item) => !state.current.blocked.has(item.id)));
          setError(null);
        }
      } catch (failure) {
        if (!controller.signal.aborted && state.current.alive) {
          setConversations([]);
          setError(failure);
          if (chatAccessLost(failure)) {
            state.current.denied = true;
            emit({ kind: 'revoked' });
          }
        }
      } finally {
        if (state.current.request === controller) state.current.request = null;
        if (state.current.alive) {
          setLoading(false);
          setFetching(false);
        }
        if (state.current.again && state.current.alive) {
          state.current.again = false;
          void refreshList();
        }
      }
    },
    [owner, emit],
  );
  const deny = useCallback(
    (id: string) => {
      state.current.blocked.add(id);
      setBlocked(new Set(state.current.blocked));
      setConversations((old) => old.filter((item) => item.id !== id));
      emit({ conversationId: id, kind: 'revoked' });
    },
    [emit],
  );
  const allow = useCallback(
    (id: string) => {
      state.current.blocked.delete(id);
      setBlocked(new Set(state.current.blocked));
      void refresh();
    },
    [refresh],
  );
  const retryAll = useCallback(() => {
    state.current.denied = false;
    setLoading(true);
    void refresh();
  }, [refresh]);
  const messageHint = useCallback(
    (event: { conversationId: string }) => {
      emit({ kind: 'messages', conversationId: event.conversationId });
      void refresh();
    },
    [emit, refresh],
  );
  const readHint = useCallback(
    (event: { conversationId: string }) => {
      emit({ kind: 'receipts', conversationId: event.conversationId });
      void refresh();
    },
    [emit, refresh],
  );
  const conversationHint = useCallback(
    (event: { conversationId: string }) => {
      emit({ kind: 'metadata', conversationId: event.conversationId });
      void refresh();
    },
    [emit, refresh],
  );
  useSignalREvent('chat', 'MessageCreated', messageHint);
  useSignalREvent('chat', 'MessagesRead', readHint);
  useSignalREvent('chat', 'ConversationUpdated', conversationHint);
  useEffect(() => {
    const current = state.current;
    current.alive = true;
    if (owner) void refresh();
    const focus = () => {
      if (!document.hidden) void refresh();
    };
    const timer = window.setInterval(focus, 20000);
    window.addEventListener('focus', focus);
    document.addEventListener('visibilitychange', focus);
    return () => {
      current.alive = false;
      current.request?.abort();
      window.clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', focus);
    };
  }, [owner, refresh]);
  useEffect(() => {
    if (owner && isConnected.chat) {
      emit({ kind: 'reconnect' });
      void refresh();
    }
  }, [owner, isConnected.chat, emit, refresh]);

  return (
    <ChatContext.Provider
      value={{
        owner,
        conversations,
        loading,
        fetching,
        error,
        connected: isConnected.chat,
        blocked,
        refresh,
        retryAll,
        deny,
        allow,
        subscribe,
        setGuard,
        confirmLeave: navigation.confirmLeave,
      }}
    >
      {children}
      {navigation.discardDialog}
    </ChatContext.Provider>
  );
}

export function ChatUnreadBadge() {
  const { conversations, error } = useChat();
  const count = conversations.reduce((sum, item) => sum + item.unreadCount, 0);
  if (error || !count) return null;
  return (
    <span
      aria-label={`Nieprzeczytane wiadomości: ${count}`}
      className="ml-auto rounded-full border bg-background px-2 py-0.5 text-[10px] font-bold text-foreground"
    >
      {count > 99 ? '99+' : count}
    </span>
  );
}
