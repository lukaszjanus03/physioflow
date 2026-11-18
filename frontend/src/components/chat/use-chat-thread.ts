'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { ChatThread, type ThreadSnapshot } from '@/lib/chat-thread';
import type { ChatMessage } from '@/lib/chat-api';
import { useChat } from './ChatProvider';

export function useChatThread(id: string, owner: string) {
  const chat = useChat();
  const model = useRef<ChatThread | null>(null);
  const [snapshot, setSnapshot] = useState<ThreadSnapshot>({
    conversation: null,
    messages: [],
    loading: true,
    busy: false,
    error: null,
    revoked: false,
    hasOlder: false,
    revision: 0,
    change: 'initial',
  });
  const { refresh: refreshList, deny, subscribe } = chat;
  useEffect(() => {
    const thread = new ChatThread(id, owner, {
      changed: () => {
        void refreshList();
      },
      denied: deny,
    });
    model.current = thread;
    const unsubscribe = thread.subscribe(() => setSnapshot(thread.snapshot()));
    const unsubscribeHints = subscribe((hint) => {
      if (hint.conversationId && hint.conversationId !== id) return;
      if (hint.kind === 'revoked') thread.revoke();
      else void thread.refresh();
    });
    const focus = () => {
      if (!document.hidden) void thread.refresh();
    };
    const timer = window.setInterval(focus, 10000);
    window.addEventListener('focus', focus);
    document.addEventListener('visibilitychange', focus);
    void thread.start();
    return () => {
      unsubscribe();
      unsubscribeHints();
      thread.stop();
      if (model.current === thread) model.current = null;
      window.clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', focus);
    };
  }, [id, owner, refreshList, deny, subscribe]);
  const refresh = useCallback(() => model.current?.refresh() ?? Promise.resolve(), []);
  const older = useCallback(() => model.current?.loadOlder() ?? Promise.resolve(), []);
  const accept = useCallback((message: ChatMessage) => model.current?.accept(message), []);
  const markVisible = useCallback((message: ChatMessage) => {
    void model.current?.markVisible(message);
  }, []);
  return { ...snapshot, refresh, older, accept, markVisible };
}
