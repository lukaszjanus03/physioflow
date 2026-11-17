import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { PaginatedList } from '@/types/api';

export type JournalStatus = 'Completed' | 'Partial' | 'Skipped';
export const journalStatuses: Record<JournalStatus, string> = {
  Completed: 'Wykonane',
  Partial: 'Częściowo wykonane',
  Skipped: 'Pominięte',
};
export type JournalReport = {
  performedOn: string;
  status: JournalStatus;
  actualSets: number | null;
  actualRepetitions: number | null;
  actualDurationSeconds: number | null;
  painLevel: number | null;
  comment: string | null;
};
export type JournalExercise = {
  name: string;
  instructions: string | null;
  order: number;
  sets: number | null;
  repetitions: number | null;
  durationSeconds: number | null;
  frequency: string | null;
  notes: string | null;
};
export type JournalEntry = JournalReport & {
  id: string;
  patientId: string;
  therapyPlanId: string;
  therapyPlanVersionId: string;
  planVersionNumber: number;
  planItemId: string;
  publishedAt: string;
  planName: string | null;
  exercise: JournalExercise;
  createdAt: string;
  updatedAt: string | null;
  version: string;
};
export type CreateJournalRequest = JournalReport & {
  requestId: string;
  therapyPlanId: string;
  planVersionNumber: number;
  planItemId: string;
};
export type JournalFilters = {
  from: string;
  to: string;
  therapyPlanId: string;
  status: JournalStatus | '';
};
const root = '/api/patient-portal/exercise-journal';
const config = (signal?: AbortSignal) => ({ signal, timeout: 15000 });

// Explicit allowlists keep immutable snapshots and clinical data out of writes.
function reportBody(value: JournalReport): JournalReport {
  return {
    performedOn: value.performedOn,
    status: value.status,
    actualSets: value.actualSets,
    actualRepetitions: value.actualRepetitions,
    actualDurationSeconds: value.actualDurationSeconds,
    painLevel: value.painLevel,
    comment: value.comment,
  };
}
export const journalApi = {
  list: (filters: JournalFilters, page: number, signal: AbortSignal) =>
    apiClient
      .get<PaginatedList<JournalEntry>>(root, {
        ...config(signal),
        params: {
          pageNumber: page,
          pageSize: 10,
          from: filters.from || undefined,
          to: filters.to || undefined,
          therapyPlanId: filters.therapyPlanId || undefined,
          status: filters.status || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: string, signal?: AbortSignal) =>
    apiClient.get<JournalEntry>(`${root}/${encodeURIComponent(id)}`, config(signal)).then((r) => r.data),
  create: (body: CreateJournalRequest, signal?: AbortSignal) =>
    apiClient
      .post<JournalEntry>(
        root,
        {
          ...reportBody(body),
          requestId: body.requestId,
          therapyPlanId: body.therapyPlanId,
          planVersionNumber: body.planVersionNumber,
          planItemId: body.planItemId,
        },
        config(signal),
      )
      .then((r) => r.data),
  update: (id: string, version: string, report: JournalReport, signal?: AbortSignal) =>
    apiClient
      .put<JournalEntry>(
        `${root}/${encodeURIComponent(id)}`,
        {
          ...reportBody(report),
          version,
        },
        config(signal),
      )
      .then((r) => r.data),
  remove: (id: string, version: string, signal?: AbortSignal) =>
    apiClient
      .delete(`${root}/${encodeURIComponent(id)}`, { ...config(signal), data: { version } })
      .then(() => undefined),
};
export function journalErrorCode(error: unknown): string | undefined {
  return isAxiosError(error) ? (error.response?.data as { code?: string } | undefined)?.code : undefined;
}
export function journalHttpStatus(error: unknown) {
  return isAxiosError(error) ? error.response?.status : undefined;
}
export function journalError(error: unknown): string {
  switch (journalErrorCode(error)) {
    case 'concurrent_modification':
      return 'Dane lub uprawnienia zmieniły się w innym miejscu. Sprawdź aktualny stan przed kolejną próbą.';
    case 'idempotency_key_reused':
      return 'Identyfikator zgłoszenia był już użyty z inną treścią. Sprawdź historię przed utworzeniem kolejnego wpisu.';
    case 'journal_entry_deleted':
      return 'To zgłoszenie dotyczy usuniętego wpisu. Nie zostanie on odtworzony.';
  }
  switch (journalHttpStatus(error)) {
    case 400:
      return 'Sprawdź datę, status, zakresy wartości i długość komentarza. Nie łącz czasu z powtórzeniami.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Dziennik wymaga aktywnego, potwierdzonego konta pacjenta.';
    case 404:
      return 'Wpis lub udostępniona wersja ćwiczenia nie są już dostępne. Twoja historia pozostaje osobną częścią dziennika.';
    default:
      return 'Nie udało się potwierdzić operacji. Nie zakładaj, że zapis się nie powiódł. Sprawdź aktualny stan lub ponów identyczne zgłoszenie.';
  }
}
