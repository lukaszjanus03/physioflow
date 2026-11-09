import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';
import { getApiMessage } from '@/lib/portal-api';

export type CooperationStatus =
  | 'Pending'
  | 'Accepted'
  | 'Rejected'
  | 'Withdrawn'
  | 'Ended';

export type CooperationParty = 'Patient' | 'Therapist';

export type CooperationTherapist = {
  id: string;
  firstName: string;
  lastName: string;
  city: string | null;
  practiceName: string | null;
};

export type PatientCooperationRequest = {
  id: string;
  status: CooperationStatus;
  message: string | null;
  createdAt: string;
  decidedAt: string | null;
  therapist: CooperationTherapist;
  endedAt: string | null;
  endedBy: CooperationParty | null;
};

export type PatientCooperation = {
  requestId: string;
  since: string;
  therapist: CooperationTherapist;
};

type CooperationProblem = {
  detail?: string;
  errors?: Record<string, string[]>;
};

export const COOPERATION_STATUS_LABELS: Record<CooperationStatus, string> = {
  Pending: 'Oczekująca',
  Accepted: 'Zaakceptowana',
  Rejected: 'Odrzucona',
  Withdrawn: 'Wycofana',
  Ended: 'Zakończona',
};

export const COOPERATION_ENDED_BY_LABELS: Record<CooperationParty, string> = {
  Patient: 'przez pacjenta',
  Therapist: 'przez terapeutę',
};

export const cooperationKeys = {
  all: (accountId: string) => ['patient-cooperation', accountId] as const,
  requests: (accountId: string) =>
    ['patient-cooperation', accountId, 'requests'] as const,
  therapists: (accountId: string) =>
    ['patient-cooperation', accountId, 'therapists'] as const,
};

export function getCooperationError(error: unknown): string {
  if (!isAxiosError<CooperationProblem>(error)) {
    return getApiMessage(error);
  }

  if (!error.response) {
    return (
      'Nie udało się połączyć z serwerem lub potwierdzić wyniku operacji. ' +
      'Sprawdź połączenie i odśwież listę przed ponownym wysłaniem.'
    );
  }

  const { status, data } = error.response;

  const messages = Object.values(data?.errors ?? {})
    .flat()
    .filter((message) => typeof message === 'string' && message.trim());

  if (messages.length > 0) {
    return [...new Set(messages)].join(' ');
  }

  // Backend podaje m.in. powod blokady i termin ponownego wyslania.
  if (
    status !== 401 &&
    status !== 403 &&
    typeof data?.detail === 'string' &&
    data.detail.trim()
  ) {
    return data.detail.trim();
  }

  if (status === 429) {
    return 'Przekroczono limit prób. Spróbuj ponownie później.';
  }

  if (status === 409) {
    return 'Stan prośby lub powiązania zmienił się. Odśwież listę i sprawdź aktualne dane.';
  }

  if (status === 404) {
    return 'Nie znaleziono wskazanej prośby, karty lub profilu. Odśwież widok.';
  }

  return getApiMessage(error);
}

const baseUrl = '/api/patient-accounts/me';

export const cooperationApi = {
  requests: (signal?: AbortSignal) =>
    apiClient
      .get<PatientCooperationRequest[]>(
        `${baseUrl}/cooperation-requests`,
        { signal, timeout: 15_000 },
      )
      .then((response) => response.data),

  therapists: (signal?: AbortSignal) =>
    apiClient
      .get<PatientCooperation[]>(`${baseUrl}/cooperations`, {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  send: (therapistId: string, message: string) =>
    apiClient
      .post<PatientCooperationRequest>(
        `${baseUrl}/cooperation-requests`,
        {
          therapistId,
          message: message.trim() || null,
        },
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  withdraw: (requestId: string) =>
    apiClient
      .post<PatientCooperationRequest>(
        `${baseUrl}/cooperation-requests/${encodeURIComponent(requestId)}/withdraw`,
        undefined,
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  end: (requestId: string) =>
    apiClient
      .post<PatientCooperationRequest>(
        `${baseUrl}/cooperations/${encodeURIComponent(requestId)}/end`,
        undefined,
        { timeout: 15_000 },
      )
      .then((response) => response.data),
};