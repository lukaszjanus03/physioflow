import { expect, test, type Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { ids, pageOf } from '../tests/fixtures/sharing';
import { exportEntry, exportPage, exportPatient } from '../tests/fixtures/patient-export';

async function mockExport(page: Page) {
  const state = {
    mode: 'normal' as 'normal' | 'empty' | 'multi' | 'failure' | 'long',
    pages: [] as number[],
  };
  await page.addInitScript(
    ({ owner }) =>
      localStorage.setItem(
        'physioflow-auth',
        JSON.stringify({
          state: {
            accessToken: 'synthetic-export-token',
            userId: owner,
            roles: ['Therapist'],
            isAuthenticated: true,
            email: 'therapist@example.test',
          },
          version: 0,
        }),
      ),
    { owner: ids.therapist },
  );
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3100',
    'access-control-allow-methods': 'GET, OPTIONS',
    'access-control-allow-headers': 'authorization, content-type',
  };
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(data) });
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    if (path === '/api/chat/conversations') return json(pageOf([]));
    if (path === `/api/patients/${ids.patient}`) return json(exportPatient);
    if (path === `/api/analytics/patients/${ids.patient}/pain-trend`)
      return json({ from: '2026-11-16', to: '2026-11-22', timeZone: 'Europe/Warsaw', points: [] });
    if (path === `/api/patients/${ids.patient}/medical-records/export`) {
      const number = Number(url.searchParams.get('pageNumber'));
      state.pages.push(number);
      if (state.mode === 'empty') return json(exportPage([]));
      if (state.mode === 'failure' && number === 2) return json({ detail: 'PRIVATE_EXPORT_FAILURE' }, 500);
      if (state.mode === 'failure' || state.mode === 'multi') {
        const all = Array.from({ length: 105 }, (_, i) => exportEntry(i + 1));
        return json(exportPage(all.slice((number - 1) * 100, number * 100), number, 105));
      }
      if (state.mode === 'long') {
        const entry = exportEntry();
        entry.content.interview =
          'Zażółć gęślą jaźń. Długi wywiad testowy bez danych rzeczywistych. '.repeat(140).slice(0, 9900) +
          ' KONIEC_WYWIADU';
        entry.content.recommendations =
          'Zalecenia testowe służą wyłącznie sprawdzeniu układu PDF. '.repeat(140).slice(0, 9900) +
          ' KONIEC_ZALECEN';
        return json(exportPage([entry, exportEntry(2, { appointmentId: null, appointmentStartsAt: null })]));
      }
      return json(exportPage([exportEntry()]));
    }
    throw new Error(`Unhandled export browser request: ${request.method()} ${path}`);
  });
  await page.goto(`/patients/${ids.patient}`);
  await page.getByRole('button', { name: 'Eksport PDF', exact: true }).click();
  return state;
}

test('card-only export downloads an actual PDF without documentation calls', async ({ page }, info) => {
  const state = await mockExport(page);
  await page.getByRole('button', { name: 'Przygotuj eksport', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Podsumowanie eksportu' })).toBeVisible();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Pobierz PDF', exact: true }).click();
  const file = await downloaded;
  const path = info.outputPath('synthetic-card.pdf');
  await file.saveAs(path);
  expect((await readFile(path)).subarray(0, 5).toString()).toBe('%PDF-');
  expect(state.pages).toEqual([]);
});

test('approved documentation generates a real multipage PDF with Polish text', async ({ page }, info) => {
  const state = await mockExport(page);
  state.mode = 'long';
  await page.getByLabel('Dołącz dokumentację wizyt', { exact: false }).check();
  await page.getByRole('button', { name: 'Przygotuj eksport', exact: true }).click();
  await expect(page.getByText(/dokumentacja: 2 wpisów/)).toBeVisible();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Pobierz PDF', exact: true }).click();
  const file = await downloaded;
  const path = info.outputPath('synthetic-documentation.pdf');
  await file.saveAs(path);
  const bytes = await readFile(path);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  expect(bytes.length).toBeGreaterThan(15000);
});

test('all pages are prepared, while a later page error never offers a partial PDF', async ({ page }) => {
  const state = await mockExport(page);
  state.mode = 'multi';
  await page.getByLabel('Dołącz dokumentację wizyt', { exact: false }).check();
  await page.getByRole('button', { name: 'Przygotuj eksport', exact: true }).click();
  await expect(page.getByText(/dokumentacja: 105 wpisów/)).toBeVisible();
  expect(state.pages).toEqual([1, 2]);
  state.mode = 'failure';
  await page.getByRole('button', { name: 'Przygotuj ponownie', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Plik nie został pobrany');
  await expect(page.getByRole('button', { name: 'Pobierz PDF', exact: true })).toHaveCount(0);
  await expect(page.getByText('PRIVATE_EXPORT_FAILURE')).toHaveCount(0);
});

test('empty and invalid ranges are explained before download on mobile and desktop', async ({ page }) => {
  const state = await mockExport(page);
  state.mode = 'empty';
  await page.getByLabel('Dołącz dokumentację wizyt', { exact: false }).check();
  await page.getByLabel('Data wizyty od', { exact: true }).fill('2026-11-02');
  await page.getByLabel('Data wizyty do', { exact: true }).fill('2026-11-01');
  await expect(page.getByRole('button', { name: 'Przygotuj eksport', exact: true })).toBeDisabled();
  await page.getByLabel('Data wizyty od', { exact: true }).fill('');
  await page.getByLabel('Data wizyty do', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Przygotuj eksport', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('status')).toContainText('Brak zatwierdzonych wpisów');
  expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
  await page.getByRole('button', { name: 'Zamknij', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
});
