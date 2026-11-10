import { format } from 'date-fns';

import type {
  CalendarInterval,
  CalendarSettings,
} from '@/lib/calendar-api';
import type { AppointmentStatus } from '@/types/api';

export const APPOINTMENT_LABELS: Record<AppointmentStatus, string> = {
  Scheduled: 'Zaplanowana',
  Completed: 'Odbyta',
  Cancelled: 'Odwołana',
  NoShow: 'Nieobecność',
};

export const APPOINTMENT_COLORS: Record<AppointmentStatus, string> = {
  Scheduled: '#0f766e',
  Completed: '#2563eb',
  Cancelled: '#64748b',
  NoShow: '#be123c',
};

const warsawParts = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/Warsaw',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

// Data pomocnicza tylko do wyswietlania w React Big Calendar.
// Nigdy nie wysylamy jej do API jako terminu wizyty.
export function toCalendarDate(value: string | Date): Date {
  const parts = warsawParts.formatToParts(new Date(value));
  const read = (type: string) =>
    Number(parts.find((part) => part.type === type)?.value ?? 0);

  return new Date(
    read('year'),
    read('month') - 1,
    read('day'),
    read('hour'),
    read('minute'),
    read('second'),
  );
}

export function calendarDateKey(value: Date): string {
  return format(value, 'yyyy-MM-dd');
}

export function warsawDateKey(value: string | Date): string {
  return calendarDateKey(toCalendarDate(value));
}

export function appointmentDateTime(value: string): string {
  return new Intl.DateTimeFormat('pl-PL', {
    timeZone: 'Europe/Warsaw',
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value));
}

export function appointmentTime(value: string): string {
  return new Intl.DateTimeFormat('pl-PL', {
    timeZone: 'Europe/Warsaw',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

export function dayIntervals(
  settings: CalendarSettings,
  date: Date,
): CalendarInterval[] {
  const key = calendarDateKey(date);
  const exception = settings.exceptions.find((item) => item.date === key);

  if (exception) {
    return exception.isDayOff ? [] : exception.intervals ?? [];
  }

  const days = [
    'Sunday',
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
  ];

  return settings.weeklyHours.find(
    (day) => day.dayOfWeek === days[date.getDay()],
  )?.intervals ?? [];
}