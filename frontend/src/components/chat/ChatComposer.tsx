'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { Send } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/primitives';
import {
  chatApi,
  chatAccessLost,
  chatCode,
  chatContentError,
  chatError,
  chatRetrySeconds,
  chatStatus,
  readOnlyText,
  type ChatConversation,
  type ChatMessage,
  type ChatSend,
} from '@/lib/chat-api';
import { useChat } from './ChatProvider';

export function ChatComposer({
  conversation,
  messages,
  accept,
  refresh,
}: {
  conversation: ChatConversation;
  messages: ChatMessage[];
  accept: (message: ChatMessage) => void;
  refresh: () => Promise<void>;
}) {
  const chat = useChat();
  const id = useId();
  const [content, setContent] = useState('');
  const [pending, setPending] = useState<ChatSend | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [wait, setWait] = useState(0);
  const [needsCheck, setNeedsCheck] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const request = useRef<AbortController | null>(null);
  const { setGuard } = chat;
  const dirty = Boolean(content || pending);
  useEffect(() => {
    setGuard({ dirty, busy });
    return () => setGuard({ dirty: false, busy: false });
  }, [dirty, busy, setGuard]);
  useEffect(() => () => request.current?.abort(), []);
  if (
    pending &&
    messages.some(
      (m) =>
        m.requestId === pending.requestId &&
        m.senderId === chat.owner &&
        m.conversationId === conversation.id &&
        m.content === pending.content,
    )
  ) {
    setContent('');
    setPending(null);
    setError('');
    setNeedsCheck(false);
    setBlocked(false);
    setWait(0);
  }
  useEffect(() => {
    if (!wait) return;
    const timer = window.setInterval(() => setWait((old) => Math.max(0, old - 1)), 1000);
    return () => window.clearInterval(timer);
  }, [wait]);

  async function send() {
    if (request.current || busy || wait || blocked || needsCheck || (!pending && !conversation.canSend))
      return;
    const validation = pending ? null : chatContentError(content);
    if (validation) {
      setError(validation);
      return;
    }
    const body = pending ?? { requestId: crypto.randomUUID(), content: content.trim() };
    setPending(body);
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError('');
    try {
      const message = await chatApi.send(conversation.id, body, controller.signal);
      if (controller.signal.aborted) return;
      accept(message);
      setContent('');
      setPending(null);
      setBlocked(false);
      setWait(0);
    } catch (failure) {
      if (controller.signal.aborted) return;
      if (chatAccessLost(failure)) {
        chat.deny(conversation.id);
        return;
      }
      setError(chatError(failure));
      setWait(chatRetrySeconds(failure));
      if (chatStatus(failure) === 400) setPending(null);
      if (chatCode(failure) === 'idempotency_key_reused') setBlocked(true);
      if (chatCode(failure) === 'concurrent_modification') setNeedsCheck(true);
      void refresh();
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return (
    <form
      className="space-y-3 border-t bg-card p-4"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        void send();
      }}
    >
      {!conversation.canSend && (
        <p role="status" className="rounded-lg bg-muted p-3 text-sm leading-6">
          {readOnlyText(conversation.readOnlyReason)}
        </p>
      )}
      {(conversation.canSend || dirty) && (
        <>
          <label htmlFor={id} className="sr-only">
            Treść wiadomości
          </label>
          <Textarea
            id={id}
            rows={3}
            value={content}
            maxLength={4000}
            placeholder="Napisz wiadomość…"
            readOnly={busy || Boolean(pending) || !conversation.canSend}
            aria-describedby={`${id}-help`}
            onChange={(event) => {
              setContent(event.target.value);
              setError('');
            }}
            onKeyDown={(event) => {
              if (
                (event.ctrlKey || event.metaKey) &&
                event.key === 'Enter' &&
                !event.nativeEvent.isComposing
              ) {
                event.preventDefault();
                void send();
              }
            }}
          />
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p id={`${id}-help`} className="text-[11px] leading-5 text-muted-foreground">
              {content.length}/4000 · Enter: nowa linia · Ctrl+Enter: wyślij
            </p>
            <Button
              type="submit"
              className="gap-2"
              disabled={
                busy ||
                Boolean(wait) ||
                blocked ||
                needsCheck ||
                (!pending && (!conversation.canSend || !content.trim()))
              }
            >
              <Send size={15} aria-hidden="true" />
              {busy ? 'Wysyłanie…' : pending ? 'Ponów tę samą wiadomość' : 'Wyślij wiadomość'}
            </Button>
          </div>
        </>
      )}
      {error && (
        <p role="alert" className="text-sm leading-6 text-destructive">
          {error}
        </p>
      )}
      {wait > 0 && (
        <p role="status" className="text-xs text-muted-foreground">
          Ponowienie dostępne za {wait} s.
        </p>
      )}
      {pending && !busy && (
        <p className="text-xs leading-6 text-muted-foreground">
          Zachowano oryginalną treść i klucz wysyłki. Przed napisaniem tej wiadomości ponownie jako nowej
          sprawdź historię. Ponowienie może potwierdzić zapis, nawet jeśli rozmowa stała się tylko do odczytu.
        </p>
      )}
      {needsCheck && (
        <Button
          type="button"
          variant="outline"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            try {
              await refresh();
              setNeedsCheck(false);
            } finally {
              setBusy(false);
            }
          }}
        >
          Odśwież stan przed ponowieniem
        </Button>
      )}
      {pending && !busy && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() =>
            chat.confirmLeave(() => {
              setPending(null);
              setContent('');
              setError('');
              setWait(0);
              setBlocked(false);
              setNeedsCheck(false);
            })
          }
        >
          Odrzuć niepewną wysyłkę
        </Button>
      )}
    </form>
  );
}
