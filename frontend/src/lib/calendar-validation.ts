export type CalendarFormErrors = Record<string, string>;

type Interval = {
  start: string;
  end: string;
};

type SettingsInput = {
  bufferMinutes: string;
  allowedDurations: number[];
  weeklyHours: { intervals: Interval[] }[];
  exceptions: {
    date: string;
    isDayOff: boolean;
    intervals: Interval[];
  }[];
};

export type AppointmentDraft = {
  patientId: string;
  date: string;
  durationMinutes: number;
  startsAt: string;
  reason: string;
  notes: string;
};

export function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) {
    return false;
  }

  const parsed = new Date(`${value}T00:00:00Z`);

  return (
    Number.isFinite(parsed.getTime()) &&
    parsed.toISOString().slice(0, 10) === value
  );
}

export function validateIntervals(intervals: Interval[]): string | null {
  if (intervals.length > 8) {
    return 'Jeden dzień może mieć maksymalnie 8 przedziałów.';
  }

  for (const interval of intervals) {
    const pattern = /^([01]\d|2[0-3]):[0-5]\d$/;

    if (!pattern.test(interval.start) || !pattern.test(interval.end)) {
      return 'Podaj poprawne godziny rozpoczęcia i zakończenia.';
    }

    if (interval.end <= interval.start) {
      return 'Zakończenie musi być późniejsze niż rozpoczęcie.';
    }
  }

  const sorted = [...intervals].sort((a, b) =>
    a.start.localeCompare(b.start),
  );

  for (let index = 1; index < sorted.length; index += 1) {
    if (sorted[index].start < sorted[index - 1].end) {
      return 'Przedziały godzinowe nie mogą się nakładać.';
    }
  }

  return null;
}

export function validateCalendarSettings(
  draft: SettingsInput,
): CalendarFormErrors {
  const errors: CalendarFormErrors = {};
  const buffer = Number(draft.bufferMinutes);

  if (
    !draft.bufferMinutes.trim() ||
    !Number.isInteger(buffer) ||
    buffer < 0 ||
    buffer > 60
  ) {
    errors.bufferMinutes = 'Podaj liczbę całkowitą od 0 do 60 minut.';
  }

  if (
    draft.allowedDurations.length === 0 ||
    draft.allowedDurations.some((value) => ![30, 45, 60].includes(value))
  ) {
    errors.allowedDurations = 'Wybierz przynajmniej jeden wariant 30/45/60 minut.';
  }

  draft.weeklyHours.forEach((day, index) => {
    const error = validateIntervals(day.intervals);

    if (error) errors[`weeklyHours[${index}].intervals`] = error;
  });

  if (draft.exceptions.length > 366) {
    errors.exceptions = 'Możesz zapisać maksymalnie 366 wyjątków.';
  }

  const dates = new Set<string>();

  draft.exceptions.forEach((exception, index) => {
    const prefix = `exceptions[${index}]`;

    if (!isCalendarDate(exception.date)) {
      errors[`${prefix}.date`] = 'Wybierz poprawną datę.';
    } else if (dates.has(exception.date)) {
      errors[`${prefix}.date`] = 'Ta data jest już na liście wyjątków.';
    } else {
      dates.add(exception.date);
    }

    if (!exception.isDayOff) {
      const error = validateIntervals(exception.intervals);

      if (exception.intervals.length === 0) {
        errors[`${prefix}.intervals`] = 'Dodaj przynajmniej jeden przedział.';
      } else if (error) {
        errors[`${prefix}.intervals`] = error;
      }
    }
  });

  return errors;
}

export function validateAppointment(
  draft: AppointmentDraft,
  allowedDurations: number[],
  now: number,
): CalendarFormErrors {
  const errors: CalendarFormErrors = {};

  if (!draft.patientId) errors.patientId = 'Wybierz pacjenta.';
  if (!isCalendarDate(draft.date)) errors.date = 'Wybierz poprawną datę.';

  if (!allowedDurations.includes(draft.durationMinutes)) {
    errors.durationMinutes = 'Wybierz dozwoloną długość wizyty.';
  }

  const startsAt = new Date(draft.startsAt).getTime();

  if (!Number.isFinite(startsAt) || startsAt <= now) {
    errors.startsAt = 'Wybierz aktualny wolny termin w przyszłości.';
  }

  if (draft.reason.trim().length > 500) {
    errors.reason = 'Powód wizyty może mieć maksymalnie 500 znaków.';
  }

  if (draft.notes.trim().length > 2000) {
    errors.notes = 'Notatka może mieć maksymalnie 2000 znaków.';
  }

  return errors;
}