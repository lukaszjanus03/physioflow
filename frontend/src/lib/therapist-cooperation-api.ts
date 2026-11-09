import { apiClient } from '@/lib/api-client';
import type { CooperationStatus } from '@/lib/cooperation-api';
import type { PaginatedList, PatientListItem } from '@/types/api';

export type CardAction = 'LinkExisting' | 'CreateNew';

export type TherapistCooperationRequest = {
  id: string;
  status: CooperationStatus;
  message: string | null;
  createdAt: string;
  decidedAt: string | null;
  patient: {
    firstName: string | null;
    lastName: string | null;
    email: string;
  };
  cardLinkMode: CardAction | null;
  linkedPatientId: string | null;
};

export type AcceptCooperationPayload = {
  identityVerified: boolean;
  cardAction: CardAction;
  patientId: string | null;
  newPatient: {
    firstName: string;
    lastName: string;
    pesel: string | null;
    dateOfBirth: string | null;
    phoneNumber: string | null;
    email: string | null;
  } | null;
};

const baseUrl = '/api/therapists/me/cooperation-requests';

export const therapistCooperationApi = {
  list: (
    status: CooperationStatus | 'All',
    pageNumber: number,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PaginatedList<TherapistCooperationRequest>>(baseUrl, {
        params: {
          status: status === 'All' ? undefined : status,
          pageNumber,
          pageSize: 10,
        },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  details: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<TherapistCooperationRequest>(
        `${baseUrl}/${encodeURIComponent(id)}`,
        { signal, timeout: 15_000 },
      )
      .then((response) => response.data),

  accept: (id: string, payload: AcceptCooperationPayload) =>
    apiClient
      .post<TherapistCooperationRequest>(
        `${baseUrl}/${encodeURIComponent(id)}/accept`,
        payload,
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  reject: (id: string) =>
    apiClient
      .post<TherapistCooperationRequest>(
        `${baseUrl}/${encodeURIComponent(id)}/reject`,
        undefined,
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  cards: (search: string, pageNumber: number, signal?: AbortSignal) =>
    apiClient
      .get<PaginatedList<PatientListItem>>('/api/patients', {
        params: {
          search,
          pageNumber,
          pageSize: 10,
          includeArchived: false,
          onlyArchived: false,
        },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),
};