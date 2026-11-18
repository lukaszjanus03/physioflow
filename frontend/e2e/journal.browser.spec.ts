import { expect, test } from '@playwright/test';
import { ids, pageOf, patientPlan } from '../tests/fixtures/sharing';
import { journalEntry, journalTarget } from '../tests/fixtures/journal';
import type { CreateJournalRequest, JournalEntry } from '../src/lib/exercise-journal-api';

// Synthetic browser integration, not a test of real backend authorization/storage.
test('patient records, edits and deletes an exercise journal entry', async ({ page }) => {
  const entries: JournalEntry[] = [];
  const publishedPlan = {
    ...patientPlan,
    publishedAt: journalTarget.publishedAt,
    content: {
      ...patientPlan.content,
      exercises: [{ ...journalTarget.exercise, id: ids.item, video: null }],
    },
  };
  const cors = {
    'access-control-allow-origin': 'http://127.0.0.1:3100',
    'access-control-allow-methods': 'GET, POST, PUT, DELETE, OPTIONS',
    'access-control-allow-headers': 'authorization, content-type',
  };
  await page.addInitScript(
    ({ patientId }) => {
      localStorage.setItem(
        'physioflow-auth',
        JSON.stringify({
          state: {
            accessToken: 'synthetic-journal-test-token',
            refreshToken: null,
            userId: patientId,
            email: 'patient@example.test',
            roles: ['Patient'],
            isAuthenticated: true,
          },
          version: 0,
        }),
      );
    },
    { patientId: ids.patient },
  );
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const method = route.request().method();
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', headers: cors, body: JSON.stringify(data) });
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    if (method === 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
    if (path === '/api/patient-accounts/me')
      return json({
        id: ids.patient,
        firstName: 'Pacjent',
        lastName: 'Testowy',
        email: 'patient@example.test',
        emailConfirmed: true,
      });
    if (path === '/api/patient-accounts/me/cooperations') return json([]);
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    if (path === '/api/chat/conversations') return json(pageOf([]));
    if (path === `/api/patient-portal/therapy-plans/${ids.plan}`) return json(publishedPlan);
    const root = '/api/patient-portal/exercise-journal';
    if (path === root && method === 'POST') {
      const body = route.request().postDataJSON() as CreateJournalRequest;
      expect(body.planItemId).toBe(ids.item);
      expect(body.planVersionNumber).toBe(1);
      expect(body.actualSets).toBeNull();
      expect(body.painLevel).toBe(0);
      expect(body).not.toHaveProperty('exercise');
      entries.push({ ...journalEntry, ...body, version: 'created-token' });
      return json(entries[0], 201);
    }
    if (path === root && method === 'GET') return json(pageOf(entries));
    if (path === `${root}/${journalEntry.id}` && method === 'PUT') {
      const body = route.request().postDataJSON();
      expect(body.version).toBe(entries[0].version);
      entries[0] = { ...entries[0], ...body, version: 'updated-token' };
      return json(entries[0]);
    }
    if (path === `${root}/${journalEntry.id}` && method === 'DELETE') {
      expect(route.request().postDataJSON()).toEqual({ version: 'updated-token' });
      entries.splice(0);
      return route.fulfill({ status: 204, headers: cors });
    }
    throw new Error(`Unhandled journal test request: ${method} ${path}`);
  });

  await page.goto(`/portal?view=plans&content=${ids.plan}`);
  await page.getByRole('button', { name: 'Zapisz wykonanie', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Zapisz wykonanie ćwiczenia' });
  await dialog.getByLabel('Status realizacji').selectOption('Skipped');
  await dialog.getByLabel('Ocena bólu (opcjonalnie)').selectOption('0');
  await dialog.getByLabel('Komentarz (opcjonalnie)').fill('Komentarz przeglądarkowy');
  await dialog.getByRole('button', { name: 'Zapisz wykonanie', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Historia realizacji planu', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Mój dziennik', exact: true })).toBeVisible();
  await expect(page.getByText('Komentarz przeglądarkowy', { exact: true })).toBeVisible();
  await expect(page.getByText(/0\/10 — brak bólu/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false);

  await page.getByRole('button', { name: 'Edytuj wpis', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Edytuj wpis dziennika' });
  await editor.getByLabel('Komentarz (opcjonalnie)').fill('Poprawiony komentarz');
  await editor.getByRole('button', { name: 'Zapisz zmiany', exact: true }).click();
  await expect(editor).toHaveCount(0);
  await expect(page.getByText('Poprawiony komentarz', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Usuń wpis', exact: true }).click();
  await page.getByRole('button', { name: 'Potwierdzam usunięcie', exact: true }).click();
  await expect(page.getByText('Poprawiony komentarz', { exact: true })).toHaveCount(0);
  await expect(
    page.getByRole('heading', { name: 'Brak wpisów dla wybranych filtrów', exact: true }),
  ).toBeVisible();
  expect(entries).toHaveLength(0);
});
