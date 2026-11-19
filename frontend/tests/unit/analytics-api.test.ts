import { expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
  analyticsApi,
  analyticsError,
  analyticsRangeError,
  currentMonth,
  painChartDays,
} from '@/lib/analytics-api';
import { apiUrl, server } from '../mocks/server';

it.each([
  { from: '', to: '2026-11-19' },
  { from: '1899-12-31', to: '1900-01-01' },
  { from: '2026-02-29', to: '2026-03-01' },
  { from: '2026-11-20', to: '2026-11-19' },
  { from: '2024-01-01', to: '2025-01-01' },
  { from: '9999-12-29', to: '9999-12-31' },
])('rejects an invalid date range: %o', (range) => expect(analyticsRangeError(range)).not.toBeNull());

it.each([
  { from: '2024-02-29', to: '2024-02-29' },
  { from: '2024-01-01', to: '2024-12-31' },
  { from: '2026-03-28', to: '2026-03-30' },
])('accepts an inclusive date range: %o', (range) => expect(analyticsRangeError(range)).toBeNull());

it('selects the current month in Warsaw, not UTC', () => {
  expect(currentMonth(new Date('2026-10-31T23:30:00Z'))).toEqual({ from: '2026-11-01', to: '2026-11-30' });
});

it('keeps zero, explicit missing ratings and gaps distinct', () => {
  const points = [
    { date: '2026-11-16', averagePainLevel: 0, ratingCount: 1 },
    { date: '2026-11-17', averagePainLevel: null, ratingCount: 0 },
    { date: '2026-11-19', averagePainLevel: 2.5, ratingCount: 2 },
  ];
  expect(painChartDays({ from: '2026-11-16', to: '2026-11-19', timeZone: 'Europe/Warsaw', points })).toEqual([
    points[0],
    points[1],
    { date: '2026-11-18', averagePainLevel: null, ratingCount: 0 },
    points[2],
  ]);
  expect(points).toHaveLength(3);
});

it('uses the server default week and sends both custom bounds without account parameters', async () => {
  const seen: string[] = [];
  server.use(
    http.get(apiUrl('/api/analytics/dashboard'), ({ request }) => {
      seen.push(new URL(request.url).search);
      return HttpResponse.json({});
    }),
    http.get(apiUrl('/api/analytics/patients/test-card/pain-trend'), ({ request }) => {
      seen.push(new URL(request.url).search);
      return HttpResponse.json({ points: [] });
    }),
  );
  const signal = new AbortController().signal;
  await analyticsApi.dashboard(null, signal);
  await analyticsApi.pain('test-card', { from: '2026-11-16', to: '2026-11-19' }, signal);
  expect(seen).toEqual(['', '?from=2026-11-16&to=2026-11-19']);
});

it('does not expose the server error body', () => {
  expect(
    analyticsError({ isAxiosError: true, response: { status: 404, data: { detail: 'SECRET' } } }),
  ).not.toContain('SECRET');
});
