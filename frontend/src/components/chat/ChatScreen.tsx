'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { MessageCircle, Plus, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { usePatientContent } from '@/hooks/use-patient-content';
import { useAuthStore } from '@/stores/auth-store';
import {
  chatAccessLost,
  chatApi,
  chatContacts,
  chatError,
  chatHref,
  chatUuid,
  type ChatRole,
} from '@/lib/chat-api';
import { useChat } from './ChatProvider';
import { ChatConversationPane } from './ChatConversationPane';

export default function ChatScreen({ role }: { role: ChatRole }) {
  const { userId, roles, isAuthenticated } = useAuthStore();
  const chat = useChat();
  if (!userId || !isAuthenticated || !roles.includes(role) || chat.owner !== userId)
    return (
      <p role="alert" className="p-6">
        Wiadomości wymagają zalogowanego konta.
      </p>
    );
  return (
    <div className="mx-auto max-w-7xl space-y-5 p-0 sm:p-2">
      <header className="space-y-3 rounded-2xl border bg-card p-5 sm:p-7">
        <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-primary">
          <MessageCircle size={17} aria-hidden="true" />
          Kontakt w ramach współpracy
        </p>
        <h1 className="text-4xl">Wiadomości</h1>
        <p className="max-w-3xl text-sm leading-7 text-muted-foreground">
          Czat nie służy do pilnego kontaktu i nie gwarantuje natychmiastowej odpowiedzi. W sytuacji nagłej
          skorzystaj z właściwej pomocy poza aplikacją.
        </p>
      </header>
      {chat.error && chatAccessLost(chat.error) ? (
        <section className="space-y-4 rounded-xl border bg-card p-6">
          <p role="alert">{chatError(chat.error)}</p>
          <Button type="button" variant="outline" onClick={chat.retryAll}>
            Sprawdź dostęp do wiadomości
          </Button>
        </section>
      ) : (
        <ChatWorkspace key={`${userId}:${role}`} owner={userId} role={role} />
      )}
    </div>
  );
}

export function ChatWorkspace({
  owner,
  role,
  compact = false,
  selection,
  onSelect,
  active = true,
}: {
  owner: string;
  role: ChatRole;
  compact?: boolean;
  selection?: string | null;
  onSelect?: (id: string | null) => void;
  active?: boolean;
}) {
  const chat = useChat();
  const router = useRouter();
  const params = useSearchParams();
  const selected = selection === undefined ? params.get('conversation') : selection;
  const cooperation = compact ? null : params.get('cooperation');
  const patient = compact ? null : params.get('patient');
  const [search, setSearch] = useState('');
  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState('');
  const request = useRef<AbortController | null>(null);
  const load = useCallback((signal: AbortSignal) => chatContacts(role, signal), [role]);
  const contacts = usePatientContent(load);
  useEffect(() => () => request.current?.abort(), []);
  const label = (id: string, fallback: string) =>
    contacts.data?.find((c) => c.cooperationId === id)?.name ?? `Rozmowa · ${fallback.slice(0, 8)}`;
  const conversations = chat.conversations.filter((item) =>
    label(item.cooperationId, item.id).toLocaleLowerCase('pl').includes(search.toLocaleLowerCase('pl')),
  );
  const activeContacts =
    contacts.data?.filter(
      (c) =>
        c.active &&
        (!patient || c.patientId === patient) &&
        (!cooperation || c.cooperationId === cooperation),
    ) ?? [];
  const selectedMeta = chat.conversations.find((c) => c.id === selected);
  const navigate = (id?: string) =>
    chat.confirmLeave(() => {
      if (onSelect) onSelect(id ?? null);
      else router.push(chatHref(role, id ? { conversation: id } : {}), { scroll: false });
    });

  async function open(cooperationId: string) {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setOpening(true);
    setOpenError('');
    try {
      const conversation = await chatApi.open(cooperationId, controller.signal);
      if (controller.signal.aborted) return;
      chat.allow(conversation.id);
      void chat.refresh();
      navigate(conversation.id);
    } catch (error) {
      if (!controller.signal.aborted) setOpenError(chatError(error));
    } finally {
      request.current = null;
      if (!controller.signal.aborted) setOpening(false);
    }
  }

  return (
    <div
      className={
        compact
          ? 'flex min-h-0 min-w-0 flex-1 flex-col gap-3'
          : 'grid min-w-0 items-start gap-4 lg:grid-cols-[300px_minmax(0,1fr)]'
      }
    >
      <aside
        aria-label="Lista rozmów"
        className={`min-w-0 overflow-hidden rounded-2xl border bg-card ${selected ? (compact ? 'hidden' : 'hidden lg:block') : 'shrink-0'}`}
      >
        <div className="space-y-3 border-b p-4">
          <div className="flex items-center justify-between">
            <h2 className="text-2xl">Twoje rozmowy</h2>
            <Button
              type="button"
              size="icon"
              variant="ghost"
              aria-label="Odśwież rozmowy"
              disabled={chat.fetching}
              onClick={() => void chat.refresh()}
            >
              <RefreshCw size={16} aria-hidden="true" />
            </Button>
          </div>
          <input
            type="search"
            aria-label="Znajdź rozmówcę"
            placeholder="Znajdź rozmówcę…"
            className="h-10 w-full rounded-lg border bg-background px-3 text-sm"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <Button type="button" variant="outline" className="w-full gap-2" onClick={() => navigate()}>
            <Plus size={15} aria-hidden="true" />
            Nowa rozmowa
          </Button>
        </div>
        {chat.loading && (
          <div className="p-4">
            <LoadingState title="Pobieranie rozmów…" />
          </div>
        )}
        {Boolean(chat.error) && (
          <p role="alert" className="p-4 text-sm text-destructive">
            {chatError(chat.error)}
          </p>
        )}
        {!chat.loading && !chat.error && !conversations.length && (
          <p className="p-5 text-sm leading-6 text-muted-foreground">
            {search
              ? 'Nie znaleziono rozmówcy.'
              : 'Nie masz jeszcze otwartych rozmów. Wybierz aktywną współpracę, aby rozpocząć.'}
          </p>
        )}
        <div className={compact ? 'max-h-[28dvh] overflow-y-auto' : 'max-h-[65dvh] overflow-y-auto'}>
          {conversations.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => navigate(item.id)}
              aria-current={selected === item.id ? 'true' : undefined}
              className={`block w-full space-y-2 border-b p-4 text-left transition-colors last:border-b-0 hover:bg-muted/30 ${selected === item.id ? 'border-l-4 border-l-primary bg-primary/5' : ''}`}
            >
              <span className="flex items-start justify-between gap-2">
                <span className="text-sm font-semibold [overflow-wrap:anywhere]">
                  {label(item.cooperationId, item.id)}
                </span>
                {item.unreadCount > 0 && (
                  <span
                    aria-label={`Nieprzeczytane: ${item.unreadCount}`}
                    className="shrink-0 rounded-full bg-primary px-2 py-0.5 text-[10px] text-primary-foreground"
                  >
                    {item.unreadCount}
                  </span>
                )}
              </span>
              <span className="line-clamp-2 block whitespace-pre-wrap text-xs leading-6 text-muted-foreground [overflow-wrap:anywhere]">
                {item.lastMessage?.content || 'Brak wiadomości'}
              </span>
              <span className="block text-[10px] text-muted-foreground">
                {item.lastMessage
                  ? new Date(item.lastMessage.sentAt).toLocaleString('pl-PL', {
                      dateStyle: 'short',
                      timeStyle: 'short',
                      timeZone: 'Europe/Warsaw',
                    })
                  : 'Nowa rozmowa'}
                {!item.canSend && ' · tylko odczyt'}
              </span>
            </button>
          ))}
        </div>
      </aside>
      {selected ? (
        !chatUuid(selected) ? (
          <p role="alert" className="p-6">
            Nieprawidłowy adres rozmowy.
          </p>
        ) : chat.blocked.has(selected) ? (
          <section className="space-y-4 rounded-xl border bg-card p-6">
            <p role="alert">Rozmowa jest niedostępna. Usunięto jej treść z widoku.</p>
            <Button type="button" variant="outline" onClick={() => chat.allow(selected)}>
              Sprawdź dostęp ponownie
            </Button>
            <Button type="button" variant="ghost" onClick={() => navigate()}>
              Wróć do rozmów
            </Button>
          </section>
        ) : (
          <ChatConversationPane
            key={`${owner}:${selected}`}
            id={selected}
            owner={owner}
            name={selectedMeta ? label(selectedMeta.cooperationId, selectedMeta.id) : 'Rozmowa'}
            back={() => navigate()}
            compact={compact}
            active={active}
          />
        )
      ) : (
        <section className={`min-w-0 space-y-5 rounded-2xl border bg-card ${compact ? 'p-4' : 'p-5 sm:p-7'}`}>
          <h2 className="text-3xl">Rozpocznij rozmowę</h2>
          <p className="text-sm leading-7 text-muted-foreground">
            Wybierz aktywną współpracę. Dawne rozmowy pozostają osobnymi historiami — nowa współpraca lub
            powiązanie nie wznawia starej rozmowy.
          </p>
          {contacts.loading && <LoadingState title="Pobieranie współprac…" />}
          {Boolean(contacts.error) && (
            <div className="space-y-3">
              <p role="alert" className="text-sm text-destructive">
                Nie udało się pobrać nazw rozmówców i aktywnych współprac.
              </p>
              <Button type="button" variant="outline" onClick={contacts.reload}>
                Pobierz współprace ponownie
              </Button>
            </div>
          )}
          {!contacts.loading && !contacts.error && !activeContacts.length && (
            <p className="rounded-xl bg-muted/30 p-4 text-sm leading-6">
              Brak aktywnej współpracy w tym kontekście. Istniejącą historię wybierz z listy rozmów.
            </p>
          )}
          <div className="space-y-3">
            {activeContacts.map((contact) => (
              <div
                key={contact.cooperationId}
                className="flex min-w-0 flex-wrap items-center justify-between gap-3 rounded-xl border p-4"
              >
                <p className="text-sm font-semibold [overflow-wrap:anywhere]">{contact.name}</p>
                <Button type="button" disabled={opening} onClick={() => void open(contact.cooperationId)}>
                  {opening ? 'Otwieranie…' : 'Otwórz rozmowę'}
                </Button>
              </div>
            ))}
          </div>
          {openError && (
            <p role="alert" className="text-sm text-destructive">
              {openError}
            </p>
          )}
        </section>
      )}
    </div>
  );
}
