import { describe, expect, it } from 'vitest';
import { delay, http, HttpResponse } from 'msw';
import {
  journalCalendarDate,
  journalUuid,
  therapistJournalApi,
  therapistJournalError,
  therapistJournalFilterError,
  therapistJournalHref,
  therapistJournalOverview,
  type TherapistJournalFilters,
} from '@/lib/therapist-journal-api';
import { server, apiUrl } from '../mocks/server';
import { ids, pageOf } from '../fixtures/sharing';
import { journalEntry } from '../fixtures/journal';

const root = `/api/patients/${ids.patient}/exercise-journal`;
const empty: TherapistJournalFilters = { from: '', to: '', status: '', therapyPlanId: '', planItemId: '' };
const summary = {
  completedCount: 0,
  partialCount: 1,
  skippedCount: 0,
  lastPerformedOn: journalEntry.performedOn,
};

describe('therapist journal HTTP contracts', () => {
  it('exposes only read operations', () => {
    expect(Object.keys(therapistJournalApi).sort()).toEqual(['entry', 'list', 'summary']);
  });
  it('sends identical filters to list and summary without pagination on summary', async () => {
    const received: Array<Record<string, string>> = [];
    server.use(
      http.get(apiUrl(root), ({ request }) => {
        received.push(Object.fromEntries(new URL(request.url).searchParams));
        return HttpResponse.json(pageOf([journalEntry], 3, 3));
      }),
      http.get(apiUrl(`${root}/summary`), ({ request }) => {
        received.push(Object.fromEntries(new URL(request.url).searchParams));
        return HttpResponse.json(summary);
      }),
    );
    const filters = {
      ...empty,
      from: '2020-01-01',
      to: '2099-01-01',
      therapyPlanId: ids.plan,
      planItemId: ids.item,
      status: 'Partial' as const,
    };
    const result = await therapistJournalOverview(ids.patient, filters, 3, new AbortController().signal);
    expect(result.coherent).toBe(true);
    expect(received).toContainEqual({ ...filters, pageNumber: '3', pageSize: '10' });
    expect(received).toContainEqual(filters);
  });
  it('omits empty filters', async () => {
    server.use(
      http.get(apiUrl(`${root}/summary`), ({ request }) => {
        expect(new URL(request.url).search).toBe('');
        return HttpResponse.json(summary);
      }),
    );
    await therapistJournalApi.summary(ids.patient, empty, new AbortController().signal);
  });
  it('uses the patient-scoped detail endpoint, not the patient portal', async () => {
    server.use(http.get(apiUrl(`${root}/${journalEntry.id}`), () => HttpResponse.json(journalEntry)));
    expect(
      await therapistJournalApi.entry(ids.patient, journalEntry.id, new AbortController().signal),
    ).toEqual(journalEntry);
  });
  it('uses the full backend summary instead of computing counts from one page', async () => {
    const totals = { completedCount: 20, partialCount: 5, skippedCount: 2, lastPerformedOn: '2026-11-17' };
    server.use(
      http.get(apiUrl(root), () =>
        HttpResponse.json({ ...pageOf([journalEntry]), totalCount: 27, totalPages: 3, hasNext: true }),
      ),
      http.get(apiUrl(`${root}/summary`), () => HttpResponse.json(totals)),
    );
    expect(
      (await therapistJournalOverview(ids.patient, empty, 1, new AbortController().signal)).summary,
    ).toEqual(totals);
  });
  it('refreshes both reads once if an edit races their totals', async () => {
    let lists = 0;
    let summaries = 0;
    server.use(
      http.get(apiUrl(root), () => {
        lists++;
        return HttpResponse.json(pageOf([journalEntry]));
      }),
      http.get(apiUrl(`${root}/summary`), () => {
        summaries++;
        return HttpResponse.json({ ...summary, partialCount: summaries === 1 ? 2 : 1 });
      }),
    );
    const result = await therapistJournalOverview(ids.patient, empty, 1, new AbortController().signal);
    expect(result.coherent).toBe(true);
    expect(lists).toBe(2);
    expect(summaries).toBe(2);
  });
  it('does not enter an infinite retry loop when totals keep changing', async () => {
    let lists = 0;
    let summaries = 0;
    server.use(
      http.get(apiUrl(root), () => {
        lists++;
        return HttpResponse.json(pageOf([journalEntry]));
      }),
      http.get(apiUrl(`${root}/summary`), () => {
        summaries++;
        return HttpResponse.json({ ...summary, partialCount: 9 });
      }),
    );
    expect(
      (await therapistJournalOverview(ids.patient, empty, 1, new AbortController().signal)).coherent,
    ).toBe(false);
    expect([lists, summaries]).toEqual([2, 2]);
  });
  it.each(['list', 'summary'] as const)(
    'rejects the whole overview if %s access is denied',
    async (which) => {
      server.use(
        http.get(apiUrl(root), async () => {
          if (which === 'list') return HttpResponse.json({}, { status: 404 });
          await delay(40);
          return HttpResponse.json(pageOf([journalEntry]));
        }),
        http.get(apiUrl(`${root}/summary`), async () => {
          if (which === 'summary') return HttpResponse.json({}, { status: 404 });
          await delay(40);
          return HttpResponse.json(summary);
        }),
      );
      await expect(
        therapistJournalOverview(ids.patient, empty, 1, new AbortController().signal),
      ).rejects.toMatchObject({ response: { status: 404 } });
    },
  );
  it('supports aborting the complete overview', async () => {
    const controller = new AbortController();
    controller.abort();
    await expect(therapistJournalOverview(ids.patient, empty, 1, controller.signal)).rejects.toMatchObject({
      code: 'ERR_CANCELED',
    });
  });
});

describe('therapist journal filters and safe presentation', () => {
  it.each(['00000000-0000-0000-0000-000000000000', 'not-an-id', ''])('rejects UUID %s', (id) => {
    expect(journalUuid(id)).toBe(false);
  });
  it('accepts a real-shaped UUID and future filter dates', () => {
    expect(journalUuid(ids.patient)).toBe(true);
    expect(
      therapistJournalFilterError({ ...empty, from: '2099-01-01', to: '2100-01-01', planItemId: ids.item }),
    ).toBeNull();
  });
  it.each(['therapyPlanId', 'planItemId'] as const)('rejects an empty GUID in %s', (key) => {
    expect(
      therapistJournalFilterError({ ...empty, [key]: '00000000-0000-0000-0000-000000000000' }),
    ).toBeTruthy();
  });
  it('rejects reversed dates and invalid position ids', () => {
    expect(therapistJournalFilterError({ ...empty, from: '2026-11-18', to: '2026-11-17' })).toBeTruthy();
    expect(therapistJournalFilterError({ ...empty, planItemId: 'library-name' })).toBeTruthy();
  });
  it('builds patient-scoped links with all filters preserved', () => {
    const href = therapistJournalHref(
      ids.patient,
      { ...empty, therapyPlanId: ids.plan, planItemId: ids.item, status: 'Skipped' },
      3,
    );
    const url = new URL(href, 'http://localhost');
    expect(url.pathname).toBe(`/patients/${ids.patient}/exercise-journal`);
    expect(Object.fromEntries(url.searchParams)).toEqual({
      therapyPlanId: ids.plan,
      planItemId: ids.item,
      status: 'Skipped',
      page: '3',
    });
  });
  it('formats date-only values without converting to an instant', () => {
    expect(journalCalendarDate('2026-11-18')).toBe('18.11.2026');
    expect(journalCalendarDate(null)).toBe('Brak wykonań');
  });
  it.each([400, 401, 403, 404, 500])('never exposes raw HTTP %s details', (status) => {
    expect(
      therapistJournalError({
        isAxiosError: true,
        response: { status, data: { detail: 'PRIVATE_COMMENT_PAIN_AND_ID' } },
      }),
    ).not.toContain('PRIVATE');
  });
});
