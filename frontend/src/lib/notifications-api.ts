import { apiClient } from '@/lib/api-client';
import type { PaginatedList } from '@/types/api';

export type NotificationItem = {
  id: string;
  type:
    | 'AppointmentBooked'
    | 'AppointmentCreated'
    | 'AppointmentRescheduled'
    | 'AppointmentCancelled';
  title: string;
  body: string;
  appointmentId: string;
  createdAt: string;
  readAt: string | null;
};

export const notificationKeys = {
  all: (accountId: string) => ['notifications', accountId] as const,

  count: (accountId: string) =>
    ['notifications', accountId, 'unread-count'] as const,

  list: (accountId: string, page: number) =>
    ['notifications', accountId, 'list', page] as const,
};

export const notificationsApi = {
  list: (pageNumber: number, signal?: AbortSignal) =>
    apiClient
      .get<PaginatedList<NotificationItem>>('/api/notifications', {
        params: { pageNumber, pageSize: 10 },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  count: (signal?: AbortSignal) =>
    apiClient
      .get<{ count: number }>('/api/notifications/unread-count', {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  read: (id: string) =>
    apiClient
      .patch<void>(
        `/api/notifications/${encodeURIComponent(id)}/read`,
        undefined,
        { timeout: 15_000 },
      )
      .then(() => undefined),

  readAll: () =>
    apiClient
      .post<void>('/api/notifications/read-all', undefined, {
        timeout: 15_000,
      })
      .then(() => undefined),
};