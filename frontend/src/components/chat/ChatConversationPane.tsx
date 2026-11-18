'use client';

import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { ArrowDown, ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { chatError, type ChatMessage } from '@/lib/chat-api';
import { useChat } from './ChatProvider';
import { useChatThread } from './use-chat-thread';
import { ChatComposer } from './ChatComposer';

export function ChatConversationPane({
  id,
  owner,
  name,
  back,
  compact = false,
  active = true,
}: {
  id: string;
  owner: string;
  name: string;
  back: () => void;
  compact?: boolean;
  active?: boolean;
}) {
  const chat = useChat();
  const thread = useChatThread(id, owner);
  const scroll = useRef<HTMLDivElement>(null);
  const nearEnd = useRef(true);
  const initialized = useRef(false);
  const anchor = useRef<{ height: number; top: number } | null>(null);
  const [newMessages, setNewMessages] = useState(false);
  const lastCount = useRef(0);
  useLayoutEffect(() => {
    const element = scroll.current;
    if (!element || !active) return;
    if (thread.change === 'older' && anchor.current) {
      element.scrollTop = anchor.current.top + element.scrollHeight - anchor.current.height;
      anchor.current = null;
    } else if (!initialized.current || nearEnd.current) {
      element.scrollTop = element.scrollHeight;
      initialized.current = true;
    } else if (thread.messages.length > lastCount.current && thread.change !== 'older') setNewMessages(true);
    lastCount.current = thread.messages.length;
  }, [thread.messages, thread.change, active]);

  const { markVisible } = thread;
  useEffect(() => {
    const root = scroll.current;
    if (!root || !thread.conversation || !active) return;
    const inspect = () => {
      if (document.hidden || !document.hasFocus()) return;
      const box = root.getBoundingClientRect();
      if (box.height <= 0) return;
      const viewport = root.closest('[data-chat-viewport]')?.getBoundingClientRect();
      const visibleTop = Math.max(0, box.top, viewport?.top ?? 0);
      const visibleBottom = Math.min(window.innerHeight, box.bottom, viewport?.bottom ?? window.innerHeight);
      let candidate: ChatMessage | undefined;
      for (const node of root.querySelectorAll<HTMLElement>('[data-message-id]')) {
        const rect = node.getBoundingClientRect();
        if (rect.height <= 0 || rect.bottom <= visibleTop || rect.top >= visibleBottom) continue;
        const message = thread.messages.find(
          (item) => item.id === node.dataset.messageId && item.recipientId === owner,
        );
        if (message && (!candidate || message.sequence > candidate.sequence)) candidate = message;
      }
      if (candidate) markVisible(candidate);
    };
    const observer =
      typeof IntersectionObserver !== 'undefined'
        ? new IntersectionObserver(inspect, { root, threshold: 0.1 })
        : null;
    for (const node of root.querySelectorAll('[data-message-id]')) observer?.observe(node);
    root.addEventListener('scroll', inspect);
    window.addEventListener('focus', inspect);
    window.addEventListener('resize', inspect);
    document.addEventListener('visibilitychange', inspect);
    const frame = window.requestAnimationFrame(inspect);
    return () => {
      observer?.disconnect();
      window.cancelAnimationFrame(frame);
      root.removeEventListener('scroll', inspect);
      window.removeEventListener('focus', inspect);
      window.removeEventListener('resize', inspect);
      document.removeEventListener('visibilitychange', inspect);
    };
  }, [thread.messages, thread.conversation, owner, markVisible, active]);

  if (thread.revoked || chat.blocked.has(id))
    return (
      <section className="space-y-4 rounded-2xl border bg-card p-6">
        <p role="alert">Rozmowa jest niedostępna. Usunięto jej treść z widoku.</p>
        <Button variant="outline" onClick={() => chat.allow(id)}>
          Sprawdź dostęp ponownie
        </Button>
      </section>
    );
  return (
    <section
      className={`min-w-0 overflow-hidden rounded-2xl border bg-card shadow-sm ${compact ? 'flex min-h-0 flex-1 flex-col' : ''}`}
      aria-label="Aktywna rozmowa"
    >
      <header className="flex min-w-0 shrink-0 items-center gap-3 border-b p-4">
        <Button type="button" variant="ghost" size="icon" aria-label="Wróć do rozmów" onClick={back}>
          <ArrowLeft size={18} aria-hidden="true" />
        </Button>
        <div className="min-w-0">
          <h2 className="text-2xl [overflow-wrap:anywhere]">{name}</h2>
          <p className="text-xs text-muted-foreground">
            {chat.connected ? 'Aktualizacje na żywo' : 'Tryb REST · aktualizacje okresowe'}
          </p>
        </div>
      </header>
      {thread.loading ? (
        <div className="p-6">
          <LoadingState title="Pobieranie rozmowy…" />
        </div>
      ) : (
        <>
          {thread.error && (
            <div className="space-y-2 border-b p-4">
              <p role="alert" className="text-sm text-destructive">
                {chatError(thread.error)}
              </p>
              <Button type="button" variant="outline" size="sm" onClick={() => void thread.refresh()}>
                Odśwież rozmowę
              </Button>
            </div>
          )}
          <div
            ref={scroll}
            role="log"
            aria-label="Historia wiadomości"
            aria-live="polite"
            aria-relevant="additions"
            className={`space-y-4 overflow-y-auto bg-muted/15 p-4 [overflow-anchor:none] ${compact ? 'min-h-[120px] flex-1' : 'h-[52dvh] min-h-[240px] sm:h-[55dvh]'}`}
            onScroll={() => {
              const element = scroll.current;
              if (element) {
                nearEnd.current = element.scrollHeight - element.scrollTop - element.clientHeight < 72;
                if (nearEnd.current) setNewMessages(false);
              }
            }}
          >
            {thread.hasOlder && (
              <div className="text-center">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={thread.busy}
                  onClick={() => {
                    const element = scroll.current;
                    if (element) anchor.current = { height: element.scrollHeight, top: element.scrollTop };
                    void thread.older();
                  }}
                >
                  Wczytaj starsze wiadomości
                </Button>
              </div>
            )}
            {!thread.messages.length && !thread.error && (
              <p className="py-12 text-center text-sm text-muted-foreground">
                To początek tej rozmowy. Możesz napisać pierwszą wiadomość.
              </p>
            )}
            {thread.messages.map((message, index) => {
              const mine = message.senderId === owner;
              const day = new Date(message.sentAt).toLocaleDateString('pl-PL', { timeZone: 'Europe/Warsaw' });
              const previous = thread.messages[index - 1];
              const previousDay = previous
                ? new Date(previous.sentAt).toLocaleDateString('pl-PL', { timeZone: 'Europe/Warsaw' })
                : null;
              return (
                <div key={message.id}>
                  {day !== previousDay && (
                    <p className="mb-4 text-center text-[11px] text-muted-foreground">{day}</p>
                  )}
                  <div
                    data-message-id={message.id}
                    className={`flex ${mine ? 'justify-end' : 'justify-start'}`}
                  >
                    <div
                      className={`max-w-[90%] rounded-2xl border px-4 py-3 sm:max-w-[80%] ${mine ? 'rounded-br-sm border-primary/20 bg-primary/10' : 'rounded-bl-sm bg-card'}`}
                    >
                      <span className="sr-only">{mine ? 'Ty: ' : 'Rozmówca: '}</span>
                      <p className="whitespace-pre-wrap text-sm leading-7 [overflow-wrap:anywhere]">
                        {message.content}
                      </p>
                      <p className="mt-2 text-right text-[10px] text-muted-foreground">
                        {new Date(message.sentAt).toLocaleTimeString('pl-PL', {
                          hour: '2-digit',
                          minute: '2-digit',
                          timeZone: 'Europe/Warsaw',
                        })}
                        {mine ? ` · ${message.readAt ? 'Przeczytano' : 'Wysłano'}` : ''}
                      </p>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          {newMessages && (
            <div className="flex justify-center border-t p-2">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                className="gap-2"
                onClick={() => {
                  if (scroll.current) scroll.current.scrollTop = scroll.current.scrollHeight;
                  nearEnd.current = true;
                  setNewMessages(false);
                }}
              >
                <ArrowDown size={14} aria-hidden="true" />
                Nowe wiadomości
              </Button>
            </div>
          )}
          {thread.conversation && (
            <ChatComposer
              conversation={thread.conversation}
              messages={thread.messages}
              accept={thread.accept}
              refresh={thread.refresh}
            />
          )}
        </>
      )}
    </section>
  );
}
