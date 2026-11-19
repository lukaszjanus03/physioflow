import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';

export const analyticsZone = 'Europe/Warsaw';
export type AnalyticsRange = { from: string; to: string };
type Period = AnalyticsRange & { timeZone: string };
export type DashboardStats = Period & {
  activePatientCount: number;
  appointments: { total: number; scheduled: number; completed: number; cancelled: number; noShow: number };
};
export type PainPoint = { date: string; averagePainLevel: number | null; ratingCount: number };
export type PainTrend = Period & { points: PainPoint[] };
export const formatPain = (value: number | null) =>
  value === null ? 'Brak oceny' : value.toLocaleString('pl-PL', { maximumFractionDigits: 2 });

const dayMs = 86_400_000;
const dayNumber = (date: string) => Date.parse(date + 'T00:00:00Z') / dayMs;
const dateOf = (day: number) => new Date(day * dayMs).toISOString().slice(0, 10);
const validDay = (date: string) =>
  /^\d{4}-\d{2}-\d{2}$/.test(date) &&
  date >= '1900-01-01' &&
  date <= '9999-12-30' &&
  Number.isFinite(dayNumber(date)) &&
  dateOf(dayNumber(date)) === date;

export function analyticsRangeError({ from, to }: AnalyticsRange) {
  if (!validDay(from) || !validDay(to)) return 'Podaj obie poprawne daty (1900–9999).';
  if (from > to) return 'Data początkowa nie może być późniejsza niż końcowa.';
  if (dayNumber(to) - dayNumber(from) >= 366) return 'Zakres może obejmować maksymalnie 366 dni.';
  return null;
}

export function currentMonth(now = new Date()): AnalyticsRange {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: analyticsZone,
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(now);
  const year = Number(parts.find((part) => part.type === 'year')?.value);
  const month = Number(parts.find((part) => part.type === 'month')?.value);
  return {
    from: new Date(Date.UTC(year, month - 1, 1)).toISOString().slice(0, 10),
    to: new Date(Date.UTC(year, month, 0)).toISOString().slice(0, 10),
  };
}

export function painChartDays(trend: PainTrend): PainPoint[] {
  if (analyticsRangeError(trend)) return [];
  const byDay = new Map(trend.points.map((point) => [point.date, point]));
  const start = dayNumber(trend.from);
  const length = dayNumber(trend.to) - start + 1;
  return Array.from({ length }, (_, index) => {
    const date = dateOf(start + index);
    return byDay.get(date) ?? { date, averagePainLevel: null, ratingCount: 0 };
  });
}

export const analyticsApi = {
  dashboard: (range: AnalyticsRange | null, signal: AbortSignal) =>
    apiClient
      .get<DashboardStats>('/api/analytics/dashboard', { params: range ?? undefined, signal, timeout: 15000 })
      .then((r) => r.data),
  pain: (id: string, range: AnalyticsRange | null, signal: AbortSignal) =>
    apiClient
      .get<PainTrend>(`/api/analytics/patients/${encodeURIComponent(id)}/pain-trend`, {
        params: range ?? undefined,
        signal,
        timeout: 15000,
      })
      .then((r) => r.data),
};

export function analyticsError(error: unknown) {
  const status = isAxiosError(error) ? error.response?.status : undefined;
  if (status === 401 || status === 403 || status === 404)
    return 'Dane analityczne są niedostępne lub nie masz do nich dostępu.';
  if (status === 400) return 'Serwer odrzucił zakres dat. Sprawdź wybrany okres.';
  return 'Nie udało się pobrać danych analitycznych.';
}
