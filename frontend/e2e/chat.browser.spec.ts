import { expect, test, type Page } from '@playwright/test';
import { chatId, conversation, cooperationId, message } from '../tests/fixtures/chat';
import { ids, pageOf } from '../tests/fixtures/sharing';
import type { ChatMessage, ChatRole, ChatSend } from '../src/lib/chat-api';

// Synthetic REST integration: no real backend, mail, accounts or chat content.
async function mockChat(page: Page, role: ChatRole) {
  const owner = role === 'Patient' ? ids.patient : ids.therapist;
  const peer = role === 'Patient' ? ids.therapist : ids.patient;
  const initial = message(1, { senderId: peer, recipientId: owner, content: 'Synthetic incoming message' });
  const state = {
    messages: [initial] as ChatMessage[],
    canSend: true,
    denied: false,
    readThrough: 0,
    failNext: false,
    writes: [] as ChatSend[],
    reads: [] as string[],
    version: 1,
  };
  await page.addInitScript(
    ({ owner, role }) => {
      localStorage.setItem(
        'physioflow-auth',
        JSON.stringify({
          state: {
            accessToken: 'synthetic-chat-token',
            refreshToken: null,
            userId: owner,
            email: 'chat@example.test',
            roles: [role],
            isAuthenticated: true,
          },
          version: 0,
        }),
      );
    },
    { owner, role },
  );
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3100',
    'access-control-allow-methods': 'GET, POST, OPTIONS',
    'access-control-allow-headers': 'authorization, content-type',
  };
  const meta = () => ({
    ...conversation,
    canSend: state.canSend,
    readOnlyReason: state.canSend ? null : 'cooperation_ended',
    lastMessage: state.messages.at(-1),
    lastSequence: state.messages.length,
    readThrough: state.readThrough,
    unreadCount: state.readThrough ? 0 : 1,
    version: String(state.version),
  });
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
    const json = (body: unknown, status = 200) =>
      route.fulfill({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    if (path === '/api/patient-accounts/me')
      return json({
        id: owner,
        firstName: 'Test',
        lastName: 'Patient',
        email: 'patient@example.test',
        emailConfirmed: true,
      });
    if (path === '/api/patient-accounts/me/cooperations') return json([]);
    if (path === `/api/patients/${ids.patient}/exercise-journal`) return json(pageOf([]));
    if (path === `/api/patients/${ids.patient}/exercise-journal/summary`)
      return json({ completedCount: 0, partialCount: 0, skippedCount: 0, lastPerformedOn: null });
    if (path === '/api/patient-accounts/me/cooperation-requests')
      return json([
        {
          id: cooperationId,
          status: 'Accepted',
          therapist: { id: 'profile-only', firstName: 'Test', lastName: 'Therapist' },
        },
      ]);
    if (path === '/api/therapists/me/cooperation-requests')
      return json(
        pageOf([
          {
            id: cooperationId,
            status: 'Accepted',
            patient: { firstName: 'Test', lastName: 'Patient', email: 'patient@example.test' },
            linkedPatientId: ids.patient,
          },
        ]),
      );
    const root = '/api/chat/conversations';
    if (path === root) return json(request.method() === 'POST' ? meta() : pageOf([meta()]));
    if (path.startsWith(`${root}/${chatId}`)) {
      if (state.denied) return json({ code: 'not_found', detail: 'PRIVATE_SERVER_ERROR' }, 404);
      if (path === `${root}/${chatId}`) return json(meta());
      if (path.endsWith('/read')) {
        const { throughMessageId } = request.postDataJSON();
        state.reads.push(throughMessageId);
        const seen = state.messages.find((item) => item.id === throughMessageId);
        if (!seen || seen.recipientId !== owner) return json({}, 404);
        if (seen.sequence > state.readThrough) {
          state.readThrough = seen.sequence;
          state.version++;
        }
        return json({
          conversationId: chatId,
          readerId: owner,
          readThrough: state.readThrough,
          unreadCount: 0,
        });
      }
      if (path.endsWith('/messages') && request.method() === 'POST') {
        const body = request.postDataJSON() as ChatSend;
        state.writes.push(body);
        if (state.failNext) {
          state.failNext = false;
          return json({ detail: 'PRIVATE_SEND_ERROR' }, 503);
        }
        const existing = state.messages.find((item) => item.requestId === body.requestId);
        if (existing) return json(existing);
        const sent = message(state.messages.length + 1, { ...body, senderId: owner, recipientId: peer });
        state.messages.push(sent);
        state.version++;
        return json(sent, 201);
      }
      if (path.endsWith('/messages')) {
        const after = url.searchParams.get('after');
        const before = url.searchParams.get('before');
        const items = state.messages.filter(
          (item) =>
            (after === null || item.sequence > Number(after)) &&
            (before === null || item.sequence < Number(before)),
        );
        return json({ items, hasMore: false, nextCursor: null, lastSequence: state.messages.length });
      }
    }
    throw new Error(`Unhandled chat browser request: ${request.method()} ${path}`);
  });
  return state;
}
for (const role of ['Patient', 'Therapist'] as const) {
  const compactHref =
    role === 'Patient' ? '/portal?view=therapists' : `/patients/${ids.patient}/exercise-journal`;
  test(`${role}: floating chat preserves a minimized draft and expands with explicit discard`, async ({
    page,
  }) => {
    await mockChat(page, role);
    await page.goto(compactHref);
    await page.getByRole('button', { name: 'Otwórz szybki czat', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Szybki czat', exact: true });
    await dialog.getByRole('button').filter({ hasText: 'Synthetic incoming message' }).click();
    const input = dialog.getByLabel('Treść wiadomości');
    await input.fill('Draft kept while minimized');
    await dialog.getByRole('button', { name: 'Zminimalizuj czat', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await page.getByRole('button', { name: 'Otwórz szybki czat', exact: true }).click();
    await expect(input).toHaveValue('Draft kept while minimized');
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    await dialog.getByRole('button', { name: 'Otwórz pełny widok czatu', exact: true }).click();
    const confirmation = page.getByRole('dialog', { name: 'Niezapisane zmiany', exact: true });
    await expect(confirmation).toBeVisible();
    await confirmation.getByRole('button', { name: 'Odrzuć zmiany', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`conversation=${chatId}`));
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Otwórz szybki czat', exact: true })).toHaveCount(0);
    await expect(page.getByLabel('Treść wiadomości')).toHaveValue('');
  });
  test(`${role}: minimized chat does not acknowledge new messages and close protects its draft`, async ({
    page,
  }) => {
    const state = await mockChat(page, role);
    await page.goto(compactHref);
    await page.getByRole('button', { name: 'Otwórz szybki czat', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Szybki czat', exact: true });
    await dialog.getByRole('button').filter({ hasText: 'Synthetic incoming message' }).click();
    await expect.poll(() => state.reads.length).toBeGreaterThan(0);
    await dialog.getByRole('button', { name: 'Zminimalizuj czat', exact: true }).click();
    const incoming = message(2, {
      senderId: role === 'Patient' ? ids.therapist : ids.patient,
      recipientId: role === 'Patient' ? ids.patient : ids.therapist,
      content: 'Incoming while minimized',
    });
    state.messages.push(incoming);
    state.version++;
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(
      page
        .locator('#floating-chat-panel [role="log"]')
        .getByText('Incoming while minimized', { exact: true }),
    ).toHaveCount(1);
    expect(state.reads).not.toContain(incoming.id);
    await page.getByRole('button', { name: 'Otwórz szybki czat', exact: true }).click();
    await expect.poll(() => state.reads.includes(incoming.id)).toBe(true);
    await dialog.getByLabel('Treść wiadomości').fill('Draft before close');
    await dialog.getByRole('button', { name: 'Zamknij czat', exact: true }).click();
    const confirmation = page.getByRole('dialog', { name: 'Niezapisane zmiany', exact: true });
    await confirmation.getByRole('button', { name: 'Zostań w formularzu', exact: true }).click();
    await expect(dialog.getByLabel('Treść wiadomości')).toHaveValue('Draft before close');
    await dialog.getByRole('button', { name: 'Zamknij czat', exact: true }).click();
    await confirmation.getByRole('button', { name: 'Odrzuć zmiany', exact: true }).click();
    await expect(dialog).toHaveCount(0);
  });
  const href =
    role === 'Patient' ? `/portal?view=messages&conversation=${chatId}` : `/messages?conversation=${chatId}`;
  test(`${role}: reads visible incoming messages and sends plain text in REST fallback`, async ({ page }) => {
    const state = await mockChat(page, role);
    await page.goto(href);
    const pane = page.getByRole('region', { name: 'Aktywna rozmowa' });
    // Section uses an accessible name and therefore becomes a landmark region.
    await expect(pane.getByText('Synthetic incoming message', { exact: true })).toBeVisible();
    await expect.poll(() => state.reads.length).toBeGreaterThan(0);
    await pane.getByLabel('Treść wiadomości').fill('<script>plain text</script>');
    await pane.getByRole('button', { name: 'Wyślij wiadomość', exact: true }).click();
    await expect(
      pane.getByRole('log').getByText('<script>plain text</script>', { exact: true }),
    ).toBeVisible();
    expect(state.writes).toHaveLength(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    const storage = await page.evaluate(() => JSON.stringify({ ...localStorage }));
    expect(storage).not.toContain('Synthetic incoming');
    expect(storage).not.toContain('plain text');
  });
  test(`${role}: retries the same uncertain send and confirms leaving a draft once`, async ({ page }) => {
    const state = await mockChat(page, role);
    await page.goto(href);
    const input = page.getByLabel('Treść wiadomości');
    await input.fill('Synthetic outgoing');
    state.failNext = true;
    await page.getByRole('button', { name: 'Wyślij wiadomość', exact: true }).click();
    await page.getByRole('button', { name: 'Ponów tę samą wiadomość', exact: true }).click();
    await expect(input).toHaveValue('');
    expect(state.writes).toHaveLength(2);
    expect(state.writes[0]).toEqual(state.writes[1]);
    await input.fill('Unsaved draft');
    await page.getByRole('button', { name: 'Wróć do rozmów', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Niezapisane zmiany' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Odrzuć zmiany', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Rozpocznij rozmowę' })).toBeVisible();
  });
  test(`${role}: readonly and revoked contexts do not retain a composer or private history`, async ({
    page,
  }) => {
    const state = await mockChat(page, role);
    state.canSend = false;
    await page.goto(href);
    await expect(page.getByRole('status').filter({ hasText: 'Współpraca zakończona' })).toBeVisible();
    await expect(page.getByLabel('Treść wiadomości')).toHaveCount(0);
    state.denied = true;
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.getByRole('log')).toHaveCount(0);
    await expect(page.getByText('Synthetic incoming message', { exact: true })).toHaveCount(0);
    await expect(page.getByText('PRIVATE_SERVER_ERROR')).toHaveCount(0);
  });
}
