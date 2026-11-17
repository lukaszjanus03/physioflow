import type { JournalEntry, JournalFilters, JournalReport, JournalStatus } from './exercise-journal-api';

export type JournalForm = {
  performedOn: string;
  status: JournalStatus;
  actualSets: string;
  actualRepetitions: string;
  actualDurationSeconds: string;
  painLevel: string;
  comment: string;
};
export type JournalErrors = Partial<Record<keyof JournalForm, string>>;

export function warsawDate(date: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat('en', {
    timeZone: 'Europe/Warsaw',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date);
  const get = (type: string) => parts.find((part) => part.type === type)!.value;
  return `${get('year')}-${get('month')}-${get('day')}`;
}
export function validJournalDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value < '1900-01-01') return false;
  const date = new Date(`${value}T12:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}
export function journalForm(entry?: JournalEntry, today = warsawDate()): JournalForm {
  return {
    performedOn: entry?.performedOn ?? today,
    status: entry?.status ?? 'Completed',
    actualSets: entry?.actualSets?.toString() ?? '',
    actualRepetitions: entry?.actualRepetitions?.toString() ?? '',
    actualDurationSeconds: entry?.actualDurationSeconds?.toString() ?? '',
    painLevel: entry?.painLevel?.toString() ?? '',
    comment: entry?.comment ?? '',
  };
}
export function validateJournal(form: JournalForm, publishedAt: string, today = warsawDate()): JournalErrors {
  const errors: JournalErrors = {};
  if (
    !validJournalDate(form.performedOn) ||
    form.performedOn > today ||
    form.performedOn < warsawDate(new Date(publishedAt))
  ) {
    errors.performedOn = 'Wybierz dzień od daty udostępnienia do dzisiaj (Europe/Warsaw).';
  }
  if (!['Completed', 'Partial', 'Skipped'].includes(form.status)) errors.status = 'Wybierz status wykonania.';
  const ranges = {
    actualSets: [1, 100],
    actualRepetitions: [1, 1000],
    actualDurationSeconds: [1, 86400],
    painLevel: [0, 10],
  } as const;
  for (const key of Object.keys(ranges) as (keyof typeof ranges)[]) {
    const value = form[key];
    const [min, max] = ranges[key];
    if (value !== '' && (!/^\d+$/.test(value) || Number(value) < min || Number(value) > max)) {
      errors[key] = `Podaj liczbę całkowitą od ${min} do ${max} lub pozostaw puste.`;
    }
  }
  if (form.actualRepetitions !== '' && form.actualDurationSeconds !== '')
    errors.actualRepetitions = 'Podaj powtórzenia albo czas, nigdy oba naraz.';
  if (
    form.status === 'Skipped' &&
    (form.actualSets !== '' || form.actualRepetitions !== '' || form.actualDurationSeconds !== '')
  )
    errors.status = 'Pominięte ćwiczenie nie może mieć dawki wykonanej.';
  if (form.comment.length > 2000) errors.comment = 'Komentarz może mieć maksymalnie 2000 znaków.';
  return errors;
}
export function journalPayload(form: JournalForm): JournalReport {
  const number = (value: string) => (value === '' ? null : Number(value));
  return {
    performedOn: form.performedOn,
    status: form.status,
    actualSets: number(form.actualSets),
    actualRepetitions: number(form.actualRepetitions),
    actualDurationSeconds: number(form.actualDurationSeconds),
    painLevel: number(form.painLevel),
    comment: form.comment.trim() || null,
  };
}
export function validateJournalFilters(filters: JournalFilters): string | null {
  if ((filters.from && !validJournalDate(filters.from)) || (filters.to && !validJournalDate(filters.to)))
    return 'Podaj poprawne daty od 1900-01-01.';
  if (filters.from && filters.to && filters.from > filters.to)
    return 'Data początkowa nie może być późniejsza od końcowej.';
  if (filters.therapyPlanId && !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(filters.therapyPlanId))
    return 'Nieprawidłowy identyfikator planu.';
  if (filters.status && !['Completed', 'Partial', 'Skipped'].includes(filters.status))
    return 'Nieprawidłowy filtr statusu.';
  return null;
}
export function journalHref(filters: JournalFilters, page = 1): string {
  const params = new URLSearchParams({ view: 'journal', page: String(page) });
  for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
  return `/portal?${params}`;
}
