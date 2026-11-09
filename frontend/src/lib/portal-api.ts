import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';

export type PatientAccount = {
  id: string;
  firstName: string | null;
  lastName: string | null;
  email: string;
  emailConfirmed: boolean;
};

export type TherapistEntry = {
  id: string;
  firstName: string;
  lastName: string;
  city: string;
  practiceName: string;
  bio: string | null;
};

export type TherapistOwnProfile = {
  id: string;
  firstName: string;
  lastName: string;
  city: string | null;
  practiceName: string | null;
  bio: string | null;
  isPublished: boolean;
  missingFieldsForPublication: string[];
};

export type TherapistProfilePayload = {
  firstName: string;
  lastName: string;
  city: string;
  practiceName: string;
  bio: string;
  isPublished: boolean;
};

export type CatalogPage = {
  items: TherapistEntry[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
};

export type ApiProblem = {
  detail?: string;
  error?: string;
  code?: string;
  errors?: Record<string, string[]>;
};

export function getApiStatus(error: unknown): number | undefined {
  return isAxiosError(error) ? error.response?.status : undefined;
}

export function getApiMessage(error: unknown): string {
  if (!isAxiosError<ApiProblem>(error)) {
    return 'Wystąpił nieoczekiwany błąd.';
  }

  if (!error.response) {
    return 'Nie udało się połączyć z serwerem. Sprawdź połączenie i spróbuj ponownie.';
  }

  if (error.response.status === 403) {
    return 'Nie masz uprawnień do tego widoku. Jeśli konto wymaga aktywacji, potwierdź adres e-mail.';
  }

  return (
    error.response.data?.detail ??
    error.response.data?.error ??
    'Nie udało się wykonać operacji. Spróbuj ponownie.'
  );
}

export const portalApi = {
  me: (signal?: AbortSignal) =>
    apiClient
      .get<PatientAccount>('/api/patient-accounts/me', {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  searchTherapists: (
    search: string,
    pageNumber: number,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<CatalogPage>('/api/therapists', {
        params: { search, pageNumber, pageSize: 6 },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  therapist: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<TherapistEntry>(`/api/therapists/${encodeURIComponent(id)}`, {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  myTherapistProfile: (signal?: AbortSignal) =>
    apiClient
      .get<TherapistOwnProfile>('/api/therapists/me/profile', {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  updateTherapistProfile: (payload: TherapistProfilePayload) =>
    apiClient
      .put<TherapistOwnProfile>('/api/therapists/me/profile', payload, {
        timeout: 15_000,
      })
      .then((response) => response.data),

  logout: () => apiClient.post('/api/auth/logout', undefined, { timeout: 10_000 }),
};