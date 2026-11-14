import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';
import type { PaginatedList } from '@/types/api';

export const recordFields = [
  {
    key: 'interview',
    label: 'Wywiad',
    limit: 10000,
  },
  {
    key: 'functionalAssessment',
    label: 'Obserwacje i ocena funkcjonalna',
    limit: 10000,
  },
  {
    key: 'performedActions',
    label: 'Wykonane działania',
    limit: 10000,
  },
  {
    key: 'recommendations',
    label: 'Zalecenia',
    limit: 10000,
  },
  {
    key: 'patientSummary',
    label: 'Podsumowanie dla pacjenta',
    limit: 2000,
  },
] as const;

export type ContentKey =
  (typeof recordFields)[number]['key'];

export type RecordContent = Record<
  ContentKey,
  string | null
>;

export type ContentForm = Record<ContentKey, string>;

export type MedicalRecord = {
  id: string;
  patientId: string;
  appointmentId: string | null;
  therapistId: string;
  status: 'Draft' | 'Approved';
  currentVersion: number;
  version: string;
  createdAt: string;
  updatedAt: string | null;
  content: RecordContent;
};

export type RecordItem = Pick<
  MedicalRecord,
  | 'id'
  | 'appointmentId'
  | 'status'
  | 'currentVersion'
  | 'createdAt'
  | 'updatedAt'
> & {
  appointmentStartsAt: string | null;
};

export type RecordVersion = {
  versionNumber: number;
  authorUserId: string;
  recordedAt: string;
  correctionReason: string | null;
  content: RecordContent;
};

export const recordOptions = {
  staleTime: 0,
  gcTime: 0,
  retry: false,
  refetchOnWindowFocus: false,
  refetchOnReconnect: false,
} as const;

export const isRecordId = (value: string) =>
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(
    value,
  );

const root = '/api/medical-records';

const patientRoot = (id: string) =>
  `/api/patients/${encodeURIComponent(id)}/medical-records`;

export const recordsApi = {
  list: (
    patientId: string,
    page: number,
    signal?: AbortSignal,
    pageSize = 10,
  ) =>
    apiClient
      .get<PaginatedList<RecordItem>>(
        patientRoot(patientId),
        {
          params: {
            pageNumber: page,
            pageSize,
          },
          signal,
          timeout: 15000,
        },
      )
      .then((response) => response.data),

  details: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<MedicalRecord>(
        `${root}/${encodeURIComponent(id)}`,
        {
          signal,
          timeout: 15000,
        },
      )
      .then((response) => response.data),

  history: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<RecordVersion[]>(
        `${root}/${encodeURIComponent(id)}/versions`,
        {
          signal,
          timeout: 15000,
        },
      )
      .then((response) => response.data),

  create: (
    patientId: string,
    appointmentId: string,
  ) =>
    apiClient
      .post<MedicalRecord>(
        patientRoot(patientId),
        { appointmentId },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  save: (
    record: MedicalRecord,
    content: ContentForm,
  ) =>
    apiClient
      .put<MedicalRecord>(
        `${root}/${record.id}`,
        {
          version: record.version,
          ...content,
        },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  approve: (record: MedicalRecord) =>
    apiClient
      .post<MedicalRecord>(
        `${root}/${record.id}/approve`,
        { version: record.version },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  correct: (
    record: MedicalRecord,
    content: ContentForm,
    correctionReason: string,
  ) =>
    apiClient
      .post<MedicalRecord>(
        `${root}/${record.id}/corrections`,
        {
          version: record.version,
          ...content,
          correctionReason,
        },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  findForAppointment: async (
    patientId: string,
    appointmentId: string,
    signal: AbortSignal,
  ) => {
    for (let page = 1; ; page += 1) {
      const result = await recordsApi.list(
        patientId,
        page,
        signal,
        100,
      );

      const match = result.items.find(
        (item) =>
          item.appointmentId === appointmentId,
      );

      if (match) return match;
      if (!result.hasNext) return null;
    }
  },
};

export function contentForm(
  content: RecordContent,
): ContentForm {
  return {
    interview: content.interview ?? '',
    functionalAssessment:
      content.functionalAssessment ?? '',
    performedActions:
      content.performedActions ?? '',
    recommendations:
      content.recommendations ?? '',
    patientSummary: content.patientSummary ?? '',
  };
}

export function validateContent(
  content: ContentForm,
  complete: boolean,
  reason?: string,
) {
  const errors: Record<string, string> = {};

  for (const field of recordFields) {
    if (content[field.key].length > field.limit) {
      errors[field.key] =
        `Maksymalnie ${field.limit} znaków.`;
    } else if (
      complete &&
      field.key !== 'patientSummary' &&
      !content[field.key].trim()
    ) {
      errors[field.key] = 'Uzupełnij to pole.';
    }
  }

  if (
    reason !== undefined &&
    (!reason.trim() || reason.length > 1000)
  ) {
    errors.correctionReason =
      'Wpisz powód korekty: od 1 do 1000 znaków.';
  }

  return errors;
}

export function recordStatus(error: unknown) {
  return isAxiosError(error)
    ? error.response?.status
    : undefined;
}

export function recordError(error: unknown) {
  if (!isAxiosError(error)) {
    return 'Nie udało się pobrać lub zapisać danych. Sprawdź połączenie.';
  }

  const body = error.response?.data as
    | { code?: string }
    | undefined;

  const messages: Record<string, string> = {
    medical_record_already_exists:
      'Dokumentacja tej wizyty już istnieje. Odśwież dane i otwórz istniejący wpis.',
    medical_record_correction_required:
      'Wpis jest zatwierdzony. Zmiana wymaga korekty.',
    medical_record_already_approved:
      'Dokumentacja została już zatwierdzona.',
    medical_record_not_approved:
      'Nie można korygować szkicu.',
    medical_record_completed_appointment_required:
      'Zatwierdzenie lub korekta wymagają wizyty oznaczonej jako odbyta.',
    concurrent_modification:
      'Wpis został zmieniony. Twoja treść pozostaje w formularzu. Porównaj ją z aktualnym wpisem.',
  };

  if (body?.code && messages[body.code]) {
    return messages[body.code];
  }

  switch (error.response?.status) {
    case 400:
      return 'Serwer odrzucił formularz. Sprawdź wymagane pola, długość treści i aktualność powiązania z wizytą.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Dokumentacja jest dostępna wyłącznie uprawnionemu terapeucie.';
    case 404:
      return 'Nie znaleziono dokumentacji, pacjenta lub powiązanej wizyty dostępnej dla Twojego konta.';
    case 409:
      return 'Stan danych się zmienił. Pobierz aktualny wpis przed kolejnym zapisem.';
    default:
      return 'Nie udało się wykonać operacji. Twoja wpisana treść nie została usunięta.';
  }
}

export function recordDate(value: string) {
  return new Intl.DateTimeFormat('pl-PL', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Europe/Warsaw',
  }).format(new Date(value));
}