'use client';

import { useEffect, useRef, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Maximize2, MessageCircle, Minus, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { chatAccessLost, chatError, chatHref, type ChatRole } from '@/lib/chat-api';
import { useAuthStore } from '@/stores/auth-store';
import { ChatUnreadBadge, useChat } from './ChatProvider';
import { ChatWorkspace } from './ChatScreen';

export default function FloatingChat() {
  const pathname = usePathname();
  const params = useSearchParams();
  const { userId, roles, isAuthenticated } = useAuthStore();
  const chat = useChat();
  if (!isAuthenticated || !userId || chat.owner !== userId) return null;
  const role: ChatRole | null =
    pathname.startsWith('/portal') && roles.includes('Patient')
      ? 'Patient'
      : roles.includes('Therapist')
        ? 'Therapist'
        : roles.includes('Patient')
          ? 'Patient'
          : null;
  if (!role || pathname === '/messages' || (pathname === '/portal' && params.get('view') === 'messages'))
    return null;
  // Changing pages uses the existing draft guard and starts a fresh compact view.
  return <ChatBubble key={`${userId}:${role}:${pathname}?${params.toString()}`} role={role} owner={userId} />;
}

function ChatBubble({ role, owner }: { role: ChatRole; owner: string }) {
  const chat = useChat();
  const router = useRouter();
  const [expanded, setExpanded] = useState(false);
  const [mounted, setMounted] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const launcher = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLElement>(null);
  const denied = Boolean(chat.error) && chatAccessLost(chat.error);
  useEffect(() => {
    if (expanded) panel.current?.focus();
  }, [expanded]);
  function minimize() {
    setExpanded(false);
    window.requestAnimationFrame(() => launcher.current?.focus());
  }
  function close() {
    chat.confirmLeave(() => {
      minimize();
      setMounted(false);
      setSelected(null);
    });
  }
  return (
    <>
      <Button
        ref={launcher}
        type="button"
        aria-label="Otwórz szybki czat"
        aria-expanded={expanded}
        aria-controls="floating-chat-panel"
        onClick={() => {
          setMounted(true);
          setExpanded(true);
        }}
        className={`${expanded ? 'hidden' : 'fixed'} bottom-[calc(5.5rem+env(safe-area-inset-bottom))] right-4 z-40 h-14 gap-3 rounded-full px-5 shadow-[0_8px_30px_-6px_hsl(var(--primary)/0.5)] md:bottom-6 md:right-6`}
      >
        <MessageCircle className="h-5 w-5" aria-hidden="true" />
        <span>Czat</span>
        <ChatUnreadBadge />
      </Button>
      {mounted && (
        <section
          ref={panel}
          id="floating-chat-panel"
          role="dialog"
          aria-label="Szybki czat"
          aria-modal="false"
          tabIndex={-1}
          hidden={!expanded}
          onKeyDown={(event) => {
            if (event.key === 'Escape' && !event.defaultPrevented) {
              event.stopPropagation();
              minimize();
            }
          }}
          className={`${expanded ? 'flex' : 'hidden'} fixed bottom-[calc(5.5rem+env(safe-area-inset-bottom))] right-3 z-40 h-[min(700px,calc(100dvh-7rem-env(safe-area-inset-bottom)))] w-[calc(100vw-1.5rem)] max-w-[420px] flex-col overflow-hidden rounded-2xl border bg-card shadow-[0_18px_70px_-15px_rgba(15,50,50,0.35)] outline-none md:bottom-6 md:right-6 md:h-[min(680px,calc(100dvh-3rem))]`}
        >
          <header className="flex shrink-0 items-center gap-3 border-b bg-primary/5 px-4 py-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground">
              <MessageCircle size={20} aria-hidden="true" />
            </span>
            <div className="min-w-0 flex-1">
              <h2 className="text-2xl">Twój czat</h2>
              <p className="text-[11px] text-muted-foreground">
                {chat.connected ? 'Aktualizacje na żywo' : 'Aktualizacje okresowe'}
              </p>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Otwórz pełny widok czatu"
              title="Otwórz pełny widok"
              onClick={() =>
                chat.confirmLeave(() =>
                  router.push(chatHref(role, selected ? { conversation: selected } : {})),
                )
              }
            >
              <Maximize2 size={16} aria-hidden="true" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Zminimalizuj czat"
              title="Zminimalizuj"
              onClick={minimize}
            >
              <Minus size={18} aria-hidden="true" />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Zamknij czat"
              title="Zamknij"
              onClick={close}
            >
              <X size={18} aria-hidden="true" />
            </Button>
          </header>
          <p className="shrink-0 border-b px-4 py-2 text-[10px] leading-5 text-muted-foreground">
            Nie służy do pilnego kontaktu. Minimalizacja zachowuje niewysłaną treść.
          </p>
          <div data-chat-viewport className="flex min-h-0 flex-1 flex-col overflow-y-auto p-2">
            {denied ? (
              <div className="space-y-3 p-4">
                <p role="alert">{chatError(chat.error)}</p>
                <Button variant="outline" onClick={chat.retryAll}>
                  Sprawdź dostęp do wiadomości
                </Button>
              </div>
            ) : (
              <ChatWorkspace
                owner={owner}
                role={role}
                compact
                selection={selected}
                onSelect={setSelected}
                active={expanded}
              />
            )}
          </div>
        </section>
      )}
    </>
  );
}
