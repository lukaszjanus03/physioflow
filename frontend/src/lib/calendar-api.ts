import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';
import { getApiMessage } from '@/lib/portal-api';

export const CALENDAR_DAYS = [
  { value: 'Monday', label: 'Poniedziałek' },
  { value: 'Tuesday', label: 'Wtorek' },
  { value: 'Wednesday', label: 'Środa' },
  { value: 'Thursday', label: 'Czwartek' },
  { value: 'Friday', label: 'Piątek' },
  { value: 'Saturday', label: 'Sobota' },
  { value: 'Sunday', label: 'Niedziela' },
] as const;

export type CalendarDay = (typeof CALENDAR_DAYS)[number]['value'];

export type CalendarInterval = {
  start: string;
  end: string;
};

export type WeeklyHours = {
  dayOfWeek: CalendarDay;
  intervals: CalendarInterval[];
};

export type CalendarException = {
  date: string;
  isDayOff: boolean;
  intervals: CalendarInterval[];
};

export type CalendarSettings = {
  bufferMinutes: number;
  allowedDurations: number[];
  weeklyHours: WeeklyHours[];
  exceptions: CalendarException[];
};

type CalendarProblem = {
  detail?: string;
  errors?: Record<string, string[]>;
};

export function getCalendarFieldErrors(
  error: unknown,
): Record<string, string> {
  if (!isAxiosError<CalendarProblem>(error)) return {};

  return Object.fromEntries(
    Object.entries(error.response?.data?.errors ?? {}).map(
      ([field, messages]) => [field, messages.join(' ')],
    ),
  );
}

export function getCalendarError(error: unknown): string {
  const messages = Object.values(getCalendarFieldErrors(error));

  if (messages.length > 0) {
    return [...new Set(messages)].join(' ');
  }

  if (isAxiosError<CalendarProblem>(error)) {
    if (!error.response) {
      return (
        'Nie udało się połączyć z serwerem lub potwierdzić zapisu. ' +
        'Twoje wpisane dane pozostają w formularzu.'
      );
    }

    const detail = error.response.data?.detail;

    if (
      error.response.status !== 401 &&
      error.response.status !== 403 &&
      typeof detail === 'string' &&
      detail.trim()
    ) {
      return detail;
    }
  }

  return getApiMessage(error);
}

export const calendarSettingsKey = (accountId: string) =>
  ['therapist-calendar-settings', accountId] as const;

export const calendarApi = {
  settings: (signal?: AbortSignal) =>
    apiClient
      .get<CalendarSettings>('/api/therapists/me/calendar', {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  updateSettings: (payload: CalendarSettings) =>
    apiClient
      .put<CalendarSettings>('/api/therapists/me/calendar', payload, {
        timeout: 15_000,
      })
      .then((response) => response.data),
};