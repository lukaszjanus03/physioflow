import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { RecordContent } from '@/lib/medical-records-api';
import type { PaginatedList, PatientDetail } from '@/types/api';

export type ExportDates = { from: string; to: string };
export type ApprovedExportRecord = {
  id: string;
  appointmentId: string | null;
  appointmentStartsAt: string | null;
  versionNumber: number;
  approvedAt: string;
  content: RecordContent;
};
export type PdfDocumentation = ExportDates & { entries: ApprovedExportRecord[] };
export type ExportProgress = { entries: number; total: number; page: number };

export function exportDateError({ from, to }: ExportDates): string | null {
  for (const value of [from, to]) {
    if (!value) continue;
    const parsed = new Date(`${value}T00:00:00Z`);
    if (
      !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
      value < '1900-01-01' ||
      Number.isNaN(parsed.getTime()) ||
      parsed.toISOString().slice(0, 10) !== value
    )
      return 'Podaj poprawne daty, nie wcześniejsze niż 01.01.1900.';
  }
  return from && to && from > to ? 'Data „od” nie może być późniejsza niż data „do”.' : null;
}

export const patientExportApi = {
  patient: (id: string, signal: AbortSignal) =>
    apiClient
      .get<PatientDetail>(`/api/patients/${encodeURIComponent(id)}`, { signal, timeout: 15000 })
      .then((r) => r.data),
  page: (id: string, dates: ExportDates, pageNumber: number, signal: AbortSignal) =>
    apiClient
      .get<PaginatedList<ApprovedExportRecord>>(
        `/api/patients/${encodeURIComponent(id)}/medical-records/export`,
        {
          signal,
          timeout: 15000,
          params: { from: dates.from || undefined, to: dates.to || undefined, pageNumber, pageSize: 100 },
        },
      )
      .then((r) => r.data),
};

export class ExportSetChanged extends Error {
  constructor() {
    super('export-set-changed');
  }
}

export async function allApprovedExportRecords(
  id: string,
  dates: ExportDates,
  signal: AbortSignal,
  progress?: (value: ExportProgress) => void,
) {
  const validation = exportDateError(dates);
  if (validation) throw new Error('invalid-export-dates');
  const entries: ApprovedExportRecord[] = [];
  const seen = new Set<string>();
  let total: number | null = null;
  for (let page = 1; ; page++) {
    signal.throwIfAborted();
    if ((page - 1) * 100 > 2147483647) throw new ExportSetChanged();
    const result = await patientExportApi.page(id, dates, page, signal);
    signal.throwIfAborted();
    total ??= result.totalCount;
    // The API is not a cross-request snapshot. Detect visible boundary changes,
    // rather than silently deduplicating or generating a partial document.
    if (
      !Number.isSafeInteger(total) ||
      total < 0 ||
      result.totalCount !== total ||
      result.pageNumber !== page ||
      result.pageSize !== 100 ||
      result.totalPages !== Math.ceil(total / 100) ||
      result.items.length !== Math.min(100, Math.max(0, total - (page - 1) * 100)) ||
      result.hasNext !== page * 100 < total
    )
      throw new ExportSetChanged();
    for (const item of result.items) {
      if (seen.has(item.id)) throw new ExportSetChanged();
      seen.add(item.id);
      entries.push(item);
    }
    progress?.({ entries: entries.length, total, page });
    if (!result.hasNext) return entries;
  }
}

export function exportAccessLost(error: unknown) {
  return isAxiosError(error) && [401, 403, 404].includes(error.response?.status ?? 0);
}
export function patientExportError(error: unknown): string {
  if (error instanceof ExportSetChanged)
    return 'Zestaw dokumentacji zmienił się podczas pobierania. Przygotuj eksport ponownie od początku.';
  if (exportAccessLost(error))
    return 'Brak dostępu do eksportu tej karty. Sprawdź uprawnienia lub zaloguj się ponownie.';
  if (isAxiosError(error) && error.response?.status === 400)
    return 'Nieprawidłowe parametry eksportu. Sprawdź zakres dat.';
  return 'Nie udało się przygotować kompletnego PDF. Plik nie został pobrany. Spróbuj ponownie; sprawdź też dostępność czcionek.';
}
