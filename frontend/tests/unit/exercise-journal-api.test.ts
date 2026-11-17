import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { journalApi, journalError } from '@/lib/exercise-journal-api';
import { journalForm, journalPayload } from '@/lib/exercise-journal-form';
import { server, apiUrl } from '../mocks/server';
import { journalEntry } from '../fixtures/journal';
import { pageOf } from '../fixtures/sharing';

const root = '/api/patient-portal/exercise-journal';
describe('journal HTTP contract', () => {
  it('sends AND filters and explicit pagination', async () => {
    let query: URLSearchParams | undefined;
    server.use(
      http.get(apiUrl(root), ({ request }) => {
        query = new URL(request.url).searchParams;
        return HttpResponse.json(pageOf([journalEntry], 2, 3));
      }),
    );
    const result = await journalApi.list(
      { from: '2020-01-01', to: '2020-01-03', status: 'Partial', therapyPlanId: journalEntry.therapyPlanId },
      2,
      new AbortController().signal,
    );
    expect(result.items).toEqual([journalEntry]);
    expect(Object.fromEntries(query!)).toEqual({
      pageNumber: '2',
      pageSize: '10',
      from: '2020-01-01',
      to: '2020-01-03',
      therapyPlanId: journalEntry.therapyPlanId,
      status: 'Partial',
    });
  });
  it('does not send empty filter parameters', async () => {
    server.use(
      http.get(apiUrl(root), ({ request }) => {
        expect([...new URL(request.url).searchParams.keys()]).toEqual(['pageNumber', 'pageSize']);
        return HttpResponse.json(pageOf([]));
      }),
    );
    await journalApi.list(
      { from: '', to: '', status: '', therapyPlanId: '' },
      1,
      new AbortController().signal,
    );
  });
  it('sends only reported values and immutable identifiers on create', async () => {
    const report = journalPayload(journalForm(journalEntry));
    const body = {
      ...report,
      requestId: 'request-1',
      therapyPlanId: journalEntry.therapyPlanId,
      planVersionNumber: 1,
      planItemId: journalEntry.planItemId,
    };
    let sent: unknown;
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(journalEntry, { status: 201 });
      }),
    );
    await journalApi.create({
      ...body,
      exercise: journalEntry.exercise,
      version: 'PRIVATE_TOKEN',
    } as typeof body);
    expect(sent).toEqual(body);
  });
  it('updates reported values without resending or changing the snapshot', async () => {
    const report = journalPayload(journalForm(journalEntry));
    let sent: unknown;
    server.use(
      http.put(apiUrl(`${root}/${journalEntry.id}`), async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ ...journalEntry, version: 'rotated-token' });
      }),
    );
    const result = await journalApi.update(journalEntry.id, journalEntry.version, {
      ...report,
      exercise: journalEntry.exercise,
    } as typeof report);
    expect(sent).toEqual({ ...report, version: journalEntry.version });
    expect(result.version).toBe('rotated-token');
  });
  it('deletes using the latest entry token in the request body', async () => {
    let sent: unknown;
    server.use(
      http.delete(apiUrl(`${root}/${journalEntry.id}`), async ({ request }) => {
        sent = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await journalApi.remove(journalEntry.id, 'latest-token');
    expect(sent).toEqual({ version: 'latest-token' });
  });
  it.each([
    'concurrent_modification',
    'idempotency_key_reused',
    'journal_entry_deleted',
    'validation_failed',
  ])('does not expose raw values for %s', (code) => {
    const message = journalError({
      isAxiosError: true,
      response: { status: 409, data: { code, detail: 'PRIVATE_COMMENT_AND_PAIN' } },
    });
    expect(message).not.toContain('PRIVATE_COMMENT_AND_PAIN');
  });
});
