import { apiClient } from '@/lib/api-client';
import type {
  Appointment,
  PaginatedList,
  PatientListItem,
} from '@/types/api';

export type AppointmentPayload = {
  patientId: string;
  startsAt: string;
  durationMinutes: number;
  reason: string | null;
  notes: string | null;
};

export type AvailableSlot = {
  startsAt: string;
  endsAt: string;
};

type AvailableSlotsResponse = {
  date: string;
  durationMinutes: number;
  slots: AvailableSlot[];
};

export type AppointmentAction = 'cancel' | 'complete' | 'no-show';

export const appointmentsApi = {
  list: (from: string, to: string, signal?: AbortSignal) =>
    apiClient
      .get<Appointment[]>('/api/appointments', {
        params: { from, to },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  details: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<Appointment>(`/api/appointments/${encodeURIComponent(id)}`, {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  slots: (
    date: string,
    durationMinutes: number,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<AvailableSlotsResponse>(
        '/api/therapists/me/calendar/available-slots',
        {
          params: { date, durationMinutes },
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  create: (payload: AppointmentPayload) =>
    apiClient
      .post<Appointment>('/api/appointments', payload, {
        timeout: 15_000,
      })
      .then((response) => response.data),

  update: (id: string, payload: AppointmentPayload) =>
    apiClient
      .put<Appointment>(
        `/api/appointments/${encodeURIComponent(id)}`,
        payload,
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  changeStatus: (id: string, action: AppointmentAction) =>
    apiClient
      .post<Appointment>(
        `/api/appointments/${encodeURIComponent(id)}/${action}`,
        undefined,
        { timeout: 15_000 },
      )
      .then((response) => response.data),

  patients: (search: string, pageNumber: number, signal?: AbortSignal) =>
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