import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
  allChatConversations,
  chatApi,
  chatContacts,
  chatContentError,
  chatUuid,
  chatHref,
  chatError,
  chatRetrySeconds,
  readOnlyText,
} from '@/lib/chat-api';
import { continuousChatSequence, mergeChatMessages } from '@/lib/chat-messages';
import { server, apiUrl } from '../mocks/server';
import { conversation, chatId, cooperationId, message } from '../fixtures/chat';
import { pageOf } from '../fixtures/sharing';

const root = '/api/chat/conversations';
const signal = () => new AbortController().signal;
describe('chat contracts and safe values', () => {
  it.each(['', '  \n', '\t', 'x\0', 'x'.repeat(4001), '😀'.repeat(2001)])(
    'rejects invalid content %#',
    (value) => {
      expect(chatContentError(value)).not.toBeNull();
    },
  );
  it.each(['hello', '  hello  ', 'x'.repeat(4000), '😀'.repeat(2000), '<script>not HTML</script>'])(
    'accepts valid text %#',
    (value) => {
      expect(chatContentError(value)).toBeNull();
    },
  );
  it.each(['', 'bad', '00000000-0000-0000-0000-000000000000', '../other'])('rejects invalid ID %s', (id) =>
    expect(chatUuid(id)).toBe(false),
  );
  it('creates role-specific URLs with identifiers only', () => {
    expect(chatUuid(chatId)).toBe(true);
    expect(chatHref('Patient', { conversation: chatId })).toBe(
      `/portal?view=messages&conversation=${chatId}`,
    );
    expect(chatHref('Therapist', { patient: cooperationId })).toBe(`/messages?patient=${cooperationId}`);
  });
  it.each([400, 401, 403, 404, 409, 429, 500])('never exposes server bodies for status %i', (status) => {
    expect(
      chatError({ isAxiosError: true, response: { status, data: { detail: 'SECRET', message: 'SECRET' } } }),
    ).not.toContain('SECRET');
  });
  it.each(['cooperation_ended', 'patient_archived', 'account_link_changed', 'participant_unavailable', null])(
    'has a readonly explanation %s',
    (reason) => expect(readOnlyText(reason).length).toBeGreaterThan(20),
  );
  it('honors rate-limit headers only for 429', () => {
    const error = (status: number, value: string) => ({
      isAxiosError: true,
      response: { status, headers: { 'retry-after': value } },
    });
    expect(chatRetrySeconds(error(429, '2.1'))).toBe(3);
    expect(chatRetrySeconds(error(429, 'bad'))).toBe(60);
    expect(chatRetrySeconds(error(429, '99999'))).toBe(3600);
    expect(chatRetrySeconds(error(500, '30'))).toBe(0);
  });
  it('uses exact write allowlists and opaque IDs', async () => {
    const received: unknown[] = [];
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        received.push(await request.json());
        return HttpResponse.json(conversation);
      }),
      http.post(apiUrl(`${root}/${chatId}/messages`), async ({ request }) => {
        received.push(await request.json());
        return HttpResponse.json(message(1));
      }),
      http.post(apiUrl(`${root}/${chatId}/read`), async ({ request }) => {
        received.push(await request.json());
        return HttpResponse.json({ readThrough: 1 });
      }),
    );
    await chatApi.open(cooperationId, signal());
    await chatApi.send(
      chatId,
      { requestId: message(1).requestId, content: 'hello', senderId: 'forged' } as Parameters<
        typeof chatApi.send
      >[1],
      signal(),
    );
    await chatApi.read(chatId, message(1).id, signal());
    expect(received).toEqual([
      { cooperationId },
      { requestId: message(1).requestId, content: 'hello' },
      { throughMessageId: message(1).id },
    ]);
  });
  it('keeps history cursor modes separate', async () => {
    const queries: Record<string, string>[] = [];
    server.use(
      http.get(apiUrl(`${root}/${chatId}/messages`), ({ request }) => {
        queries.push(Object.fromEntries(new URL(request.url).searchParams));
        return HttpResponse.json({ items: [] });
      }),
    );
    await chatApi.history(chatId, { after: 0 }, signal());
    await chatApi.history(chatId, { before: 51 }, signal());
    expect(queries).toEqual([
      { after: '0', limit: '50' },
      { before: '51', limit: '50' },
    ]);
  });
  it('loads every list page and deduplicates conversations', async () => {
    const pages: string[] = [];
    server.use(
      http.get(apiUrl(root), ({ request }) => {
        const page = new URL(request.url).searchParams.get('pageNumber')!;
        pages.push(page);
        return HttpResponse.json({ ...pageOf([conversation]), hasNext: page === '1' });
      }),
    );
    expect(await allChatConversations(signal())).toEqual([conversation]);
    expect(pages).toEqual(['1', '2']);
  });
  it('maps contact names by cooperation, not therapist profile ID', async () => {
    server.use(
      http.get(apiUrl('/api/patient-accounts/me/cooperation-requests'), () =>
        HttpResponse.json([
          {
            id: cooperationId,
            status: 'Accepted',
            therapist: { id: 'profile-not-user', firstName: 'Test', lastName: 'Therapist' },
            message: 'PRIVATE_REQUEST',
          },
          { id: 'pending', status: 'Pending', therapist: {} },
        ]),
      ),
    );
    expect(await chatContacts('Patient', signal())).toEqual([
      { cooperationId, name: 'Test Therapist', active: true, patientId: null },
    ]);
  });
  it('does not advance over gaps or regress read receipts', () => {
    const merged = mergeChatMessages(
      [message(1, { readAt: '2026-11-18T14:00:00Z' }), message(3)],
      [message(1), message(2), message(3)],
    );
    expect(merged.map((m) => m.sequence)).toEqual([1, 2, 3]);
    expect(merged[0].readAt).not.toBeNull();
    expect(continuousChatSequence([message(1), message(3)], 0)).toBe(1);
    expect(continuousChatSequence([message(151), message(152), message(155)], 150)).toBe(152);
  });
});
