import { expect, test, type Page } from '@playwright/test';
import { ids, pageOf } from '../tests/fixtures/sharing';
import { journalEntry } from '../tests/fixtures/journal';

async function mockTherapistJournal(page: Page) {
  const state = { denied: false, methods: [] as string[] };
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3100',
    'access-control-allow-methods': 'GET, OPTIONS',
    'access-control-allow-headers': 'authorization, content-type',
  };
  await page.addInitScript(
    ({ id }) => {
      localStorage.setItem(
        'physioflow-auth',
        JSON.stringify({
          state: {
            accessToken: 'synthetic-therapist-journal-token',
            refreshToken: null,
            userId: id,
            email: 'therapist@example.test',
            roles: ['Therapist'],
            isAuthenticated: true,
          },
          version: 0,
        }),
      );
    },
    { id: ids.therapist },
  );
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const method = route.request().method();
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    if (method === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, headers: cors, contentType: 'application/json', body: JSON.stringify(data) });
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    const root = `/api/patients/${ids.patient}/exercise-journal`;
    if (path === root || path.startsWith(`${root}/`)) {
      state.methods.push(method);
      expect(method).toBe('GET');
      if (state.denied) return json({ detail: 'PRIVATE_DENIED_DETAIL' }, 404);
      const skipped = url.searchParams.get('status') === 'Skipped';
      if (path === `${root}/summary`)
        return json(
          skipped
            ? { completedCount: 0, partialCount: 0, skippedCount: 1, lastPerformedOn: null }
            : {
                completedCount: 0,
                partialCount: 1,
                skippedCount: 0,
                lastPerformedOn: journalEntry.performedOn,
              },
        );
      if (path === root)
        return json(
          pageOf([
            {
              ...journalEntry,
              ...(skipped ? { status: 'Skipped', actualSets: null, actualRepetitions: null } : {}),
            },
          ]),
        );
      if (path === `${root}/${journalEntry.id}`) return json({ ...journalEntry, painLevel: 0 });
    }
    throw new Error(`Unhandled therapist browser request: ${method} ${path}`);
  });
  return state;
}

test('therapist reads snapshot details without any mutation controls', async ({ page }) => {
  const state = await mockTherapistJournal(page);
  await page.goto(`/patients/${ids.patient}/exercise-journal`);
  await expect(page.getByText(journalEntry.exercise.name, { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Szczegóły wpisu', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Szczegóły wpisu dziennika' });
  await expect(dialog.getByText('Historyczna instrukcja do testów.', { exact: true })).toBeVisible();
  await expect(dialog.getByText(/0\/10 — brak bólu/)).toBeVisible();
  await expect(dialog.getByRole('button', { name: /Edytuj|Usuń wpis|Zapisz/ })).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Zamknij szczegóły', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.methods.every((method) => method === 'GET')).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false);
});

test('status filter refreshes list and summary together', async ({ page }) => {
  await mockTherapistJournal(page);
  await page.goto(`/patients/${ids.patient}/exercise-journal?therapyPlanId=${ids.plan}`);
  await expect(page.getByText(journalEntry.exercise.name, { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Status realizacji', exact: true }).selectOption('Skipped');
  await page.getByRole('button', { name: 'Zastosuj', exact: true }).click();
  await expect(page).toHaveURL(/status=Skipped/);
  await expect(page).toHaveURL(new RegExp(`therapyPlanId=${ids.plan}`));
  await expect(page.getByText('Brak wykonań', { exact: true })).toBeVisible();
  await expect(page.getByRole('article').getByText('Pominięte', { exact: true })).toBeVisible();
});

test('denied details clear the whole patient preview', async ({ page }) => {
  const state = await mockTherapistJournal(page);
  await page.goto(`/patients/${ids.patient}/exercise-journal`);
  await expect(page.getByText(journalEntry.exercise.name, { exact: true })).toBeVisible();
  state.denied = true;
  await page.getByRole('button', { name: 'Szczegóły wpisu', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa', exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText(journalEntry.exercise.name, { exact: true })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Podsumowanie realizacji' })).toHaveCount(0);
  await expect(page.getByText('PRIVATE_DENIED_DETAIL', { exact: true })).toHaveCount(0);
  state.denied = false;
  await page.getByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa', exact: true }).click();
  await expect(page.getByText(journalEntry.exercise.name, { exact: true })).toBeVisible();
});
