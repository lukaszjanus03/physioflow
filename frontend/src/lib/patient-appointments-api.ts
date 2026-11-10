import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';
import type { AvailableSlot } from '@/lib/appointments-api';
import { getCalendarError } from '@/lib/calendar-api';
import type {
  Appointment,
  AppointmentStatus,
} from '@/types/api';

export type PatientAppointment = {
  id: string;
  therapist: {
    id: string;
    firstName: string;
    lastName: string;
    practiceName: string | null;
    city: string | null;
    phoneNumber: string | null;
  };
  startsAt: string;
  endsAt: string;
  durationMinutes: number;
  status: AppointmentStatus;
  canCancel: boolean;
  cancellationDeadline: string;
  patientMessage?: string | null;
};

export type PatientBookingPayload = {
  therapistId: string;
  startsAt: string;
  durationMinutes: number;
  patientMessage?: string | null;
};

export type PatientBookingSettings = {
  therapistId: string;
  timeZone: string;
  allowedDurations: number[];
};

export const patientAppointmentKeys = {
  all: (accountId: string) =>
    ['patient-appointments', accountId] as const,

  summary: (accountId: string) =>
    ['patient-appointments', accountId, 'summary'] as const,

  details: (accountId: string, id: string) =>
    ['patient-appointments', accountId, 'details', id] as const,
};

export function patientAppointmentError(
  error: unknown,
): string {
  if (isAxiosError(error) && !error.response) {
    return (
      'Nie udało się połączyć z serwerem lub potwierdzić wyniku operacji. ' +
      'Sprawdź listę wizyt przed ponownym wysłaniem rezerwacji.'
    );
  }

  return getCalendarError(error);
}

export function patientAppointmentErrorCode(
  error: unknown,
): string | undefined {
  return isAxiosError<{ code?: string }>(error)
    ? error.response?.data?.code
    : undefined;
}

const baseUrl = '/api/patient-portal/appointments';

export const patientAppointmentsApi = {
  list: (
    from: string,
    to: string,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PatientAppointment[]>(baseUrl, {
        params: { from, to },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  details: (
    id: string,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PatientAppointment>(
        `${baseUrl}/${encodeURIComponent(id)}`,
        {
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  summary: (signal?: AbortSignal) =>
    apiClient
      .get<{
        nextAppointment: PatientAppointment | null;
      }>(
        `${baseUrl}/summary`,
        {
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  settings: (
    therapistId: string,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PatientBookingSettings>(
        `${baseUrl}/therapists/${encodeURIComponent(therapistId)}/settings`,
        {
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  slots: (
    therapistId: string,
    date: string,
    durationMinutes: number,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<{
        date: string;
        durationMinutes: number;
        slots: AvailableSlot[];
      }>(
        `${baseUrl}/therapists/${encodeURIComponent(therapistId)}/available-slots`,
        {
          params: {
            date,
            durationMinutes,
          },
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  book: (payload: PatientBookingPayload) =>
    apiClient
      .post<PatientAppointment>(
        baseUrl,
        payload,
        {
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  cancel: (id: string) =>
    apiClient
      .post<PatientAppointment>(
        `${baseUrl}/${encodeURIComponent(id)}/cancel`,
        undefined,
        {
          timeout: 15_000,
        },
      )
      .then((response) => response.data),
};

export const therapistSummaryKey = (
  accountId: string,
) =>
  [
    'therapist-appointments',
    accountId,
    'summary',
  ] as const;

export const therapistAppointmentSummaryApi = (
  signal?: AbortSignal,
) =>
  apiClient
    .get<{
      upcomingAppointments: Appointment[];
    }>(
      '/api/appointments/summary',
      {
        signal,
        timeout: 15_000,
      },
    )
    .then((response) => response.data);