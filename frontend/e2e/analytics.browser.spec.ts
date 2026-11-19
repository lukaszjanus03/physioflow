import { expect, test, type Page } from '@playwright/test';
import { exportPatient } from '../tests/fixtures/patient-export';
import { ids, pageOf } from '../tests/fixtures/sharing';

async function mockAnalytics(page: Page) {
  const state = { denied: false, ranges: [] as string[] };
  await page.addInitScript((userId) => {
    localStorage.setItem(
      'physioflow-auth',
      JSON.stringify({
        version: 0,
        state: { userId, roles: ['Therapist'], isAuthenticated: true, accessToken: 'synthetic-token' },
      }),
    );
  }, ids.therapist);
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    const headers = {
      'access-control-allow-origin': 'http://127.0.0.1:3100',
      'access-control-allow-methods': 'GET, OPTIONS',
      'access-control-allow-headers': 'authorization, content-type',
    };
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    const send = (json: unknown, status = 200) => route.fulfill({ json, status, headers });
    if (path === '/api/notifications/unread-count') return send({ count: 0 });
    if (path === '/api/chat/conversations' || path === '/api/patients') return send(pageOf([]));
    if (path === '/api/appointments/summary') return send({ upcomingAppointments: [] });
    if (path === `/api/patients/${ids.patient}`) return send(exportPatient);
    const period = { from: '2026-11-16', to: '2026-11-22', timeZone: 'Europe/Warsaw' };
    if (path === '/api/analytics/dashboard') {
      state.ranges.push(url.search);
      const custom = url.searchParams.has('from');
      return send({
        ...period,
        ...(custom ? { from: url.searchParams.get('from'), to: url.searchParams.get('to') } : {}),
        activePatientCount: 12,
        appointments: {
          total: custom ? 0 : 8,
          scheduled: custom ? 0 : 4,
          completed: custom ? 0 : 2,
          cancelled: custom ? 0 : 1,
          noShow: custom ? 0 : 1,
        },
      });
    }
    if (path === `/api/analytics/patients/${ids.patient}/pain-trend`) {
      if (state.denied) return send({ detail: 'INTERNAL_DENIAL' }, 404);
      return send({
        ...period,
        points: [
          { date: '2026-11-16', averagePainLevel: 0, ratingCount: 1 },
          { date: '2026-11-17', averagePainLevel: null, ratingCount: 0 },
        ],
      });
    }
    throw new Error(`Unhandled analytics test request: ${path}`);
  });
  return state;
}

test('dashboard uses server aggregates and applies custom Warsaw dates', async ({ page }) => {
  const state = await mockAnalytics(page);
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { name: 'Mój gabinet', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Godziny pracy', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2 })).toHaveText([
    'Najbliższe wizyty',
    'Skróty',
    'Pacjenci',
    'Statystyki gabinetu',
  ]);
  const panel = page.getByRole('region', { name: 'Statystyki gabinetu' });
  await expect(panel.getByText('12', { exact: true })).toBeVisible();
  await expect(panel.getByText('8', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: 'Własne daty' }).click();
  await panel.getByLabel('Od', { exact: true }).fill('2026-11-20');
  await panel.getByLabel('Do', { exact: true }).fill('2026-11-19');
  await expect(panel.getByRole('button', { name: 'Zastosuj zakres' })).toBeDisabled();
  await panel.getByLabel('Od', { exact: true }).fill('2026-11-19');
  await panel.getByRole('button', { name: 'Zastosuj zakres' }).click();
  await expect(panel.getByText('Brak wizyt w wybranym zakresie.')).toBeVisible();
  expect(state.ranges).toContain('?from=2026-11-19&to=2026-11-19');
});

test('patient chart renders a single zero point and clears on revoked access', async ({ page }) => {
  const state = await mockAnalytics(page);
  await page.goto(`/patients/${ids.patient}`);
  const panel = page.getByRole('region', { name: 'Zgłaszany ból' });
  await expect(panel.locator('.recharts-line-dot')).toHaveCount(1);
  await panel.getByText('Tabela ocen bólu').click();
  await expect(panel.getByRole('row', { name: /17\.11\.2026/ })).toContainText('Brak oceny');
  await expect(
    panel.getByRole('row', { name: /16\.11\.2026/ }).getByRole('cell', { name: '0', exact: true }),
  ).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
  state.denied = true;
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(panel.getByRole('alert')).toContainText('niedostępne');
  await expect(panel.getByRole('table')).toHaveCount(0);
  await expect(panel.locator('.recharts-line-dot')).toHaveCount(0);
});
