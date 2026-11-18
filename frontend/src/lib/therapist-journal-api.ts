import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { JournalEntry, JournalFilters } from '@/lib/exercise-journal-api';
import { validateJournalFilters } from '@/lib/exercise-journal-form';
import type { PaginatedList } from '@/types/api';

export type TherapistJournalFilters = JournalFilters & { planItemId: string };
export type JournalSummary = {
  completedCount: number;
  partialCount: number;
  skippedCount: number;
  lastPerformedOn: string | null;
};
export type TherapistJournalOverview = {
  list: PaginatedList<JournalEntry>;
  summary: JournalSummary;
  coherent: boolean;
};

export function journalUuid(value: string): boolean {
  return (
    /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
  );
}
export function therapistJournalFilterError(filters: TherapistJournalFilters): string | null {
  const error = validateJournalFilters(filters);
  if (error) return error;
  if (filters.therapyPlanId && !journalUuid(filters.therapyPlanId)) return 'Nieprawidłowy filtr planu.';
  if (filters.planItemId && !journalUuid(filters.planItemId)) return 'Nieprawidłowy filtr pozycji ćwiczenia.';
  return null;
}
export function therapistJournalHref(patientId: string, filters: TherapistJournalFilters, page = 1) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filterParams(filters))) if (value) params.set(key, value);
  if (page > 1) params.set('page', String(page));
  const query = params.toString();
  return `/patients/${encodeURIComponent(patientId)}/exercise-journal${query ? `?${query}` : ''}`;
}
function filterParams(filters: TherapistJournalFilters) {
  return {
    from: filters.from || undefined,
    to: filters.to || undefined,
    therapyPlanId: filters.therapyPlanId || undefined,
    planItemId: filters.planItemId || undefined,
    status: filters.status || undefined,
  };
}
const root = (patientId: string) => `/api/patients/${encodeURIComponent(patientId)}/exercise-journal`;
const config = (signal: AbortSignal) => ({ signal, timeout: 15000 });

// Keep this client read-only even though entry DTOs contain a concurrency token.
export const therapistJournalApi = {
  list: (patientId: string, filters: TherapistJournalFilters, page: number, signal: AbortSignal) =>
    apiClient
      .get<PaginatedList<JournalEntry>>(root(patientId), {
        ...config(signal),
        params: { ...filterParams(filters), pageNumber: page, pageSize: 10 },
      })
      .then((r) => r.data),
  summary: (patientId: string, filters: TherapistJournalFilters, signal: AbortSignal) =>
    apiClient
      .get<JournalSummary>(`${root(patientId)}/summary`, {
        ...config(signal),
        params: filterParams(filters),
      })
      .then((r) => r.data),
  entry: (patientId: string, entryId: string, signal: AbortSignal) =>
    apiClient
      .get<JournalEntry>(`${root(patientId)}/${encodeURIComponent(entryId)}`, config(signal))
      .then((r) => r.data),
};

export async function therapistJournalOverview(
  patientId: string,
  filters: TherapistJournalFilters,
  page: number,
  signal: AbortSignal,
): Promise<TherapistJournalOverview> {
  const controller = new AbortController();
  const abort = () => controller.abort();
  if (signal.aborted) abort();
  signal.addEventListener('abort', abort, { once: true });
  try {
    const read = () =>
      Promise.all([
        therapistJournalApi.list(patientId, filters, page, controller.signal),
        therapistJournalApi.summary(patientId, filters, controller.signal),
      ]);
    const coherent = (list: PaginatedList<JournalEntry>, summary: JournalSummary) =>
      list.totalCount === summary.completedCount + summary.partialCount + summary.skippedCount;
    let [list, summary] = await read();
    // Separate API reads may race an edit. Retry the pair once, never loop forever.
    if (!coherent(list, summary)) [list, summary] = await read();
    return { list, summary, coherent: coherent(list, summary) };
  } catch (error) {
    controller.abort();
    throw error;
  } finally {
    signal.removeEventListener('abort', abort);
  }
}

export function therapistJournalError(error: unknown): string {
  const status = isAxiosError(error) ? error.response?.status : undefined;
  switch (status) {
    case 400:
      return 'Sprawdź zakres dat i wybrane filtry dziennika.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Nie masz uprawnień do podglądu dziennika tego pacjenta.';
    case 404:
      return 'Podgląd jest niedostępny. Wpis, karta lub uprawnienia mogły się zmienić.';
    default:
      return 'Nie udało się pobrać dziennika. Sprawdź połączenie i spróbuj ponownie.';
  }
}

export function journalCalendarDate(date: string | null): string {
  return date ? date.split('-').reverse().join('.') : 'Brak wykonań';
}
