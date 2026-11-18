import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import { cooperationApi } from '@/lib/cooperation-api';
import { therapistCooperationApi } from '@/lib/therapist-cooperation-api';
import type { PaginatedList } from '@/types/api';

export type ChatRole = 'Patient' | 'Therapist';
export type ChatMessage = {
  id: string;
  conversationId: string;
  sequence: number;
  senderId: string;
  recipientId: string;
  requestId: string;
  content: string;
  sentAt: string;
  readAt: string | null;
};
export type ChatConversation = {
  id: string;
  cooperationId: string;
  patientUserId: string;
  therapistUserId: string;
  canSend: boolean;
  readOnlyReason: string | null;
  lastMessage: ChatMessage | null;
  unreadCount: number;
  lastSequence: number;
  readThrough: number;
  version: string;
};
export type ChatHistory = {
  items: ChatMessage[];
  hasMore: boolean;
  nextCursor: number | null;
  lastSequence: number;
};
export type ChatRead = { conversationId: string; readerId: string; readThrough: number; unreadCount: number };
export type ChatSend = { requestId: string; content: string };
export type ChatContact = { cooperationId: string; name: string; active: boolean; patientId: string | null };
export type ChatHint = {
  conversationId?: string;
  kind: 'messages' | 'receipts' | 'metadata' | 'reconnect' | 'revoked';
};
const root = '/api/chat/conversations';
export function chatUuid(id: string) {
  return (
    /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id) && id !== '00000000-0000-0000-0000-000000000000'
  );
}
const config = (signal?: AbortSignal) => ({ signal, timeout: 15000 });
const path = (id: string) => `${root}/${encodeURIComponent(id)}`;
export const chatApi = {
  list: (page: number, signal: AbortSignal) =>
    apiClient
      .get<PaginatedList<ChatConversation>>(root, {
        ...config(signal),
        params: { pageNumber: page, pageSize: 100 },
      })
      .then((r) => r.data),
  conversation: (id: string, signal: AbortSignal) =>
    apiClient.get<ChatConversation>(path(id), config(signal)).then((r) => r.data),
  open: (cooperationId: string, signal: AbortSignal) =>
    apiClient.post<ChatConversation>(root, { cooperationId }, config(signal)).then((r) => r.data),
  history: (id: string, cursor: { before?: number; after?: number }, signal: AbortSignal) =>
    apiClient
      .get<ChatHistory>(`${path(id)}/messages`, {
        ...config(signal),
        params: { before: cursor.before, after: cursor.after, limit: 50 },
      })
      .then((r) => r.data),
  send: (id: string, body: ChatSend, signal: AbortSignal) =>
    apiClient
      .post<ChatMessage>(
        `${path(id)}/messages`,
        {
          requestId: body.requestId,
          content: body.content,
        },
        config(signal),
      )
      .then((r) => r.data),
  read: (id: string, throughMessageId: string, signal: AbortSignal) =>
    apiClient.post<ChatRead>(`${path(id)}/read`, { throughMessageId }, config(signal)).then((r) => r.data),
};
export async function allChatConversations(signal: AbortSignal): Promise<ChatConversation[]> {
  const items = new Map<string, ChatConversation>();
  for (let page = 1; page <= 100; page++) {
    const result = await chatApi.list(page, signal);
    for (const item of result.items) items.set(item.id, item);
    if (!result.hasNext) return [...items.values()];
  }
  throw new Error('chat-list-limit');
}
export async function chatContacts(role: ChatRole, signal: AbortSignal): Promise<ChatContact[]> {
  if (role === 'Patient') {
    return (await cooperationApi.requests(signal))
      .filter((r) => r.status === 'Accepted' || r.status === 'Ended')
      .map((r) => ({
        cooperationId: r.id,
        name: `${r.therapist.firstName} ${r.therapist.lastName}`,
        active: r.status === 'Accepted',
        patientId: null,
      }));
  }
  const contacts: ChatContact[] = [];
  for (let page = 1; page <= 100; page++) {
    const result = await therapistCooperationApi.list('All', page, signal);
    for (const r of result.items)
      if (r.status === 'Accepted' || r.status === 'Ended')
        contacts.push({
          cooperationId: r.id,
          name: [r.patient.firstName, r.patient.lastName].filter(Boolean).join(' ') || r.patient.email,
          active: r.status === 'Accepted',
          patientId: r.linkedPatientId,
        });
    if (!result.hasNext) return contacts;
  }
  throw new Error('chat-contacts-limit');
}
export function chatHref(
  role: ChatRole,
  params: { conversation?: string; cooperation?: string; patient?: string } = {},
) {
  const query = new URLSearchParams(role === 'Patient' ? { view: 'messages' } : {});
  for (const [key, value] of Object.entries(params)) if (value) query.set(key, value);
  const suffix = query.toString();
  return `${role === 'Patient' ? '/portal' : '/messages'}${suffix ? `?${suffix}` : ''}`;
}
export function chatContentError(content: string): string | null {
  if (content.length > 4000) return 'Wiadomość może mieć maksymalnie 4000 znaków.';
  if (!content.trim()) return 'Wpisz treść wiadomości.';
  if (content.includes('\0')) return 'Wiadomość zawiera niedozwolony znak.';
  return null;
}
export function chatStatus(error: unknown) {
  return isAxiosError(error) ? error.response?.status : undefined;
}
export function chatCode(error: unknown): string | undefined {
  return isAxiosError(error) ? (error.response?.data as { code?: string } | undefined)?.code : undefined;
}
export function chatAccessLost(error: unknown) {
  return [401, 403, 404].includes(chatStatus(error) ?? 0);
}
export function chatRetrySeconds(error: unknown): number {
  if (!isAxiosError(error) || error.response?.status !== 429) return 0;
  const value = Number(error.response.headers?.['retry-after']);
  return Number.isFinite(value) && value > 0 ? Math.min(3600, Math.ceil(value)) : 60;
}
export function readOnlyText(reason: string | null) {
  switch (reason) {
    case 'cooperation_ended':
      return 'Współpraca zakończona. Historia jest dostępna tylko do odczytu.';
    case 'patient_archived':
      return 'Karta pacjenta jest w archiwum. Wysyłanie jest zablokowane.';
    case 'account_link_changed':
      return 'Powiązanie konta zmieniło się. Ta rozmowa pozostaje tylko do odczytu.';
    case 'participant_unavailable':
      return 'Uczestnik jest obecnie niedostępny. Nie można wysyłać nowych wiadomości.';
    default:
      return 'Ta rozmowa jest dostępna tylko do odczytu.';
  }
}
export function chatError(error: unknown): string {
  const code = chatCode(error);
  if (
    ['cooperation_ended', 'patient_archived', 'account_link_changed', 'participant_unavailable'].includes(
      code ?? '',
    )
  )
    return readOnlyText(code ?? null);
  if (code === 'idempotency_key_reused')
    return 'Ten identyfikator był już użyty do innej wiadomości. Sprawdź historię; nie ponawiaj ze zmienioną treścią.';
  if (code === 'concurrent_modification')
    return 'Stan rozmowy zmienił się. Odśwież go i ponów identyczne zgłoszenie.';
  if (code === 'chat_not_active')
    return 'Rozpoczęcie rozmowy wymaga aktywnej i poprawnie powiązanej współpracy.';
  switch (chatStatus(error)) {
    case 400:
      return 'Sprawdź treść i parametry wiadomości.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
    case 404:
      return 'Rozmowa jest niedostępna lub nie masz do niej dostępu.';
    case 429:
      return 'Osiągnięto limit wysyłania. Odczekaj wskazany czas i ponów tę samą wiadomość.';
    default:
      return 'Nie udało się potwierdzić operacji. Przy wysyłaniu ponów tę samą wiadomość z zachowanym identyfikatorem.';
  }
}
