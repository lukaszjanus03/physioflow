import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
  allApprovedExportRecords,
  ExportSetChanged,
  exportDateError,
  patientExportApi,
  patientExportError,
} from '@/lib/patient-export-api';
import { server, apiUrl } from '../mocks/server';
import { ids } from '../fixtures/sharing';
import { exportEntry, exportPage, exportPatient } from '../fixtures/patient-export';

const path = `/api/patients/${ids.patient}/medical-records/export`;
const empty = { from: '', to: '' };
const signal = () => new AbortController().signal;
describe('PDF export validation', () => {
  it.each([
    empty,
    { from: '1900-01-01', to: '' },
    { from: '', to: '9999-12-31' },
    { from: '2024-02-29', to: '2024-02-29' },
  ])('accepts valid inclusive and open-ended ranges %#', (dates) =>
    expect(exportDateError(dates)).toBeNull(),
  );
  it.each([
    '1899-12-31',
    '2026-02-29',
    '2026-02-30',
    '2026-13-01',
    '2026-1-01',
    'garbage',
    '2026-01-01T00:00:00Z',
  ])('rejects invalid date %s', (value) => expect(exportDateError({ from: value, to: '' })).not.toBeNull());
  it('rejects a reversed range', () =>
    expect(exportDateError({ from: '2026-11-02', to: '2026-11-01' })).not.toBeNull());
  it.each([400, 401, 403, 404, 500])('does not expose raw errors %i', (status) =>
    expect(
      patientExportError({
        isAxiosError: true,
        response: { status, data: { detail: 'SECRET', errors: ['SECRET'] } },
      }),
    ).not.toContain('SECRET'),
  );
});
describe('complete export HTTP reads', () => {
  it('loads all 105 items in server order with identical date filters', async () => {
    const all = Array.from({ length: 105 }, (_, i) => exportEntry(i + 1));
    const requests: Record<string, string>[] = [];
    const progress: number[] = [];
    server.use(
      http.get(apiUrl(path), ({ request }) => {
        const params = new URL(request.url).searchParams;
        requests.push(Object.fromEntries(params));
        const page = Number(params.get('pageNumber'));
        return HttpResponse.json(exportPage(all.slice((page - 1) * 100, page * 100), page, 105));
      }),
    );
    const result = await allApprovedExportRecords(
      ids.patient,
      { from: '2026-10-01', to: '2026-10-31' },
      signal(),
      (p) => progress.push(p.entries),
    );
    expect(result.map((entry) => entry.id)).toEqual(all.map((entry) => entry.id));
    expect(requests).toEqual(
      [1, 2].map((pageNumber) => ({
        from: '2026-10-01',
        to: '2026-10-31',
        pageSize: '100',
        pageNumber: String(pageNumber),
      })),
    );
    expect(progress).toEqual([100, 105]);
  });
  it('does not send empty dates and preserves an undated legacy record', async () => {
    server.use(
      http.get(apiUrl(path), ({ request }) => {
        const params = new URL(request.url).searchParams;
        expect(params.has('from')).toBe(false);
        expect(params.has('to')).toBe(false);
        return HttpResponse.json(
          exportPage([exportEntry(1, { appointmentId: null, appointmentStartsAt: null })]),
        );
      }),
    );
    expect((await allApprovedExportRecords(ids.patient, empty, signal()))[0].appointmentStartsAt).toBeNull();
  });
  it('returns a valid empty result', async () => {
    server.use(http.get(apiUrl(path), () => HttpResponse.json(exportPage([]))));
    expect(await allApprovedExportRecords(ids.patient, empty, signal())).toEqual([]);
  });
  it('rejects instead of returning a partial document when page 2 fails', async () => {
    server.use(
      http.get(apiUrl(path), ({ request }) =>
        new URL(request.url).searchParams.get('pageNumber') === '1'
          ? HttpResponse.json(
              exportPage(
                Array.from({ length: 100 }, (_, i) => exportEntry(i + 1)),
                1,
                105,
              ),
            )
          : HttpResponse.json({}, { status: 503 }),
      ),
    );
    await expect(allApprovedExportRecords(ids.patient, empty, signal())).rejects.toMatchObject({
      response: { status: 503 },
    });
  });
  it.each(['count', 'duplicate', 'short-page', 'hasNext', 'pageNumber'])(
    'rejects an inconsistent or changed result: %s',
    async (kind) => {
      server.use(
        http.get(apiUrl(path), ({ request }) => {
          const page = Number(new URL(request.url).searchParams.get('pageNumber'));
          if (page === 1)
            return HttpResponse.json(
              exportPage(
                Array.from({ length: 100 }, (_, i) => exportEntry(i + 1)),
                1,
                105,
              ),
            );
          const second = exportPage(
            Array.from({ length: 5 }, (_, i) => exportEntry(i + 101)),
            2,
            105,
          );
          if (kind === 'count') second.totalCount++;
          if (kind === 'duplicate') second.items[0] = exportEntry(1);
          if (kind === 'short-page') second.items.pop();
          if (kind === 'hasNext') second.hasNext = true;
          if (kind === 'pageNumber') second.pageNumber = 1;
          return HttpResponse.json(second);
        }),
      );
      await expect(allApprovedExportRecords(ids.patient, empty, signal())).rejects.toBeInstanceOf(
        ExportSetChanged,
      );
    },
  );
  it('stops between pages after cancellation', async () => {
    const controller = new AbortController();
    let calls = 0;
    server.use(
      http.get(apiUrl(path), () => {
        calls++;
        return HttpResponse.json(
          exportPage(
            Array.from({ length: 100 }, (_, i) => exportEntry(i + 1)),
            1,
            105,
          ),
        );
      }),
    );
    await expect(
      allApprovedExportRecords(ids.patient, empty, controller.signal, () => controller.abort()),
    ).rejects.toBeDefined();
    expect(calls).toBe(1);
  });
  it('reads the card without calling the unused server-side PDF stub', async () => {
    server.use(http.get(apiUrl(`/api/patients/${ids.patient}`), () => HttpResponse.json(exportPatient)));
    expect(await patientExportApi.patient(ids.patient, signal())).toEqual(exportPatient);
  });
});
