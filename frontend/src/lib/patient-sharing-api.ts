import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { PaginatedList } from '@/types/api';
import type { MedicalRecord } from '@/lib/medical-records-api';
import type { TherapyPlan } from '@/lib/therapy-api';

export type PatientPublication = {
  versionNumber: number;
  patientUserId: string;
  publishedAt: string;
  isAccessible: boolean;
};
export type PatientPlanItem = {
  id: string;
  order: number;
  name: string;
  instructions: string | null;
  sets: number | null;
  repetitions: number | null;
  durationSeconds: number | null;
  frequency: string | null;
  notes: string | null;
  video: { source: 'Upload' | 'YouTube'; youtubeUrl: string | null } | null;
};
export type PatientPlanListItem = {
  id: string;
  therapistId: string;
  name: string | null;
  versionNumber: number;
  startDate: string | null;
  endDate: string | null;
  publishedAt: string;
};
export type PatientPlan = Pick<
  PatientPlanListItem,
  'id' | 'therapistId' | 'versionNumber' | 'publishedAt'
> & {
  content: {
    name: string | null;
    goal: string | null;
    startDate: string | null;
    endDate: string | null;
    generalRecommendations: string | null;
    exercises: PatientPlanItem[];
  };
};
export type PatientSummaryListItem = {
  id: string;
  therapistId: string;
  appointmentId: string;
  appointmentStartsAt: string;
  versionNumber: number;
  publishedAt: string;
};
export type PatientSummary = PatientSummaryListItem & { summary: string };
const portal = '/api/patient-portal';
const config = (signal?: AbortSignal) => ({ signal, timeout: 15000 });
export const patientSharingApi = {
  plans: (page: number, signal: AbortSignal) =>
    apiClient
      .get<
        PaginatedList<PatientPlanListItem>
      >(`${portal}/therapy-plans`, { ...config(signal), params: { pageNumber: page, pageSize: 10 } })
      .then((r) => r.data),
  plan: (id: string, signal: AbortSignal) =>
    apiClient
      .get<PatientPlan>(`${portal}/therapy-plans/${encodeURIComponent(id)}`, config(signal))
      .then((r) => r.data),
  summaries: (page: number, signal: AbortSignal) =>
    apiClient
      .get<
        PaginatedList<PatientSummaryListItem>
      >(`${portal}/visit-summaries`, { ...config(signal), params: { pageNumber: page, pageSize: 10 } })
      .then((r) => r.data),
  summary: (id: string, signal: AbortSignal) =>
    apiClient
      .get<PatientSummary>(`${portal}/visit-summaries/${encodeURIComponent(id)}`, config(signal))
      .then((r) => r.data),
  playback: (planId: string, itemId: string, signal: AbortSignal) =>
    apiClient
      .get<{
        url: string;
        expiresAt: string;
      }>(`${portal}/therapy-plans/${encodeURIComponent(planId)}/exercises/${encodeURIComponent(itemId)}/video/playback`, config(signal))
      .then((r) => r.data),
  publishPlan: (entity: TherapyPlan, number: number, signal: AbortSignal) =>
    apiClient
      .post<TherapyPlan>(
        `/api/therapy-plans/${entity.id}/publish`,
        { version: entity.version, versionNumber: number },
        config(signal),
      )
      .then((r) => r.data),
  revokePlan: (entity: TherapyPlan, signal: AbortSignal) =>
    apiClient
      .post<TherapyPlan>(
        `/api/therapy-plans/${entity.id}/revoke`,
        { version: entity.version },
        config(signal),
      )
      .then((r) => r.data),
  publishSummary: (entity: MedicalRecord, number: number, signal: AbortSignal) =>
    apiClient
      .post<MedicalRecord>(
        `/api/medical-records/${entity.id}/patient-summary/publish`,
        { version: entity.version, versionNumber: number },
        config(signal),
      )
      .then((r) => r.data),
  revokeSummary: (entity: MedicalRecord, signal: AbortSignal) =>
    apiClient
      .post<MedicalRecord>(
        `/api/medical-records/${entity.id}/patient-summary/revoke`,
        { version: entity.version },
        config(signal),
      )
      .then((r) => r.data),
};
export function sharingStatus(error: unknown) {
  return isAxiosError(error) ? error.response?.status : undefined;
}
export function accessDenied(error: unknown) {
  return [401, 403, 404].includes(sharingStatus(error) ?? 0);
}
export function needsSharingRefresh(error: unknown) {
  const status = sharingStatus(error);
  const code = isAxiosError(error)
    ? (error.response?.data as { code?: string } | undefined)?.code
    : undefined;
  return !status || code === 'concurrent_modification' || status >= 500;
}
export function sharingError(error: unknown) {
  const code = isAxiosError(error)
    ? (error.response?.data as { code?: string } | undefined)?.code
    : undefined;
  const messages: Record<string, string> = {
    concurrent_modification:
      'Dane zmieniły się w innym miejscu. Pobierz aktualny stan i ponownie świadomie wybierz operację. Niczego nie ponawiamy automatycznie.',
    patient_archived:
      'Nowe udostępnienie jest zablokowane dla archiwalnej karty. Wcześniejsze udostępnienie można cofnąć.',
    patient_account_required:
      'Publikacja wymaga powiązanego, aktywnego konta pacjenta z potwierdzonym adresem e-mail.',
    cooperation_not_active: 'Publikacja wymaga aktywnej współpracy z tym kontem i kartą pacjenta.',
    plan_not_publishable:
      'Publikować można wersje planu aktywnego lub zakończonego. Szkic i archiwum nie pozwalają na nowe publikacje.',
    medical_record_not_approved: 'Najpierw zatwierdź dokumentację wizyty.',
    medical_record_completed_appointment_required:
      'Dokumentacja musi dotyczyć zgodnie powiązanej, odbytej wizyty.',
    patient_summary_empty: 'Wybrana zatwierdzona wersja nie zawiera podsumowania dla pacjenta.',
    video_not_uploaded: 'Ten materiał jest filmem YouTube, nie prywatnym nagraniem.',
    storage_unavailable:
      'Nagranie jest chwilowo niedostępne. Instrukcja tekstowa pozostaje dostępna.',
  };
  if (code && messages[code]) return messages[code];
  switch (sharingStatus(error)) {
    case 400:
      return 'Sprawdź wybraną wersję i parametry żądania.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Brak dostępu. Konto pacjenta musi być aktywne, z potwierdzonym adresem e-mail.';
    case 404:
      return 'Treść jest niedostępna. Udostępnienie mogło zostać cofnięte lub powiązanie konta zmienione.';
    case 409:
      return 'Stan danych zmienił się. Odśwież szczegóły przed kolejną operacją.';
    case 503:
      return 'Materiał jest chwilowo niedostępny. Spróbuj później.';
    default:
      return 'Nie udało się potwierdzić operacji. Sprawdź połączenie i pobierz aktualny stan przed ponowną próbą.';
  }
}
