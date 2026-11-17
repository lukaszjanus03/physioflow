import { expect, test } from '@playwright/test';
import { ids, pageOf, patientPlan, patientSummary } from '../tests/fixtures/sharing';

// Browser integration tests with a mocked API, NOT backend authorization tests.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(
    ({ patientId }) => {
      localStorage.setItem(
        'physioflow-auth',
        JSON.stringify({
          state: {
            accessToken: 'synthetic-browser-test-token',
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
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/')) {
      // Allow only the application itself; fonts/assets are served by Next.
      if (url.origin === 'http://127.0.0.1:3100') return route.continue();
      return route.abort();
    }
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 204, headers: corsHeaders });
    }
    const json = (data: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: 'application/json',
        headers: corsHeaders,
        body: JSON.stringify(data),
      });
    if (path === '/api/patient-accounts/me') {
      return json({
        id: ids.patient,
        firstName: 'Pacjent',
        lastName: 'Testowy',
        email: 'patient@example.test',
        emailConfirmed: true,
      });
    }
    if (path === '/api/patient-accounts/me/cooperations') return json([]);
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    if (path === '/api/patient-portal/therapy-plans') {
      return json(pageOf([{ ...patientPlan, name: patientPlan.content.name }]));
    }
    if (path === `/api/patient-portal/therapy-plans/${ids.plan}`) return json(patientPlan);
    if (path === '/api/patient-portal/visit-summaries') return json(pageOf([patientSummary]));
    if (path === `/api/patient-portal/visit-summaries/${ids.record}`) {
      return json({
        ...patientSummary,
        interview: 'PRIVATE_INTERVIEW',
        recommendations: 'PRIVATE_RECOMMENDATIONS',
      });
    }
    // Never fall through to a local or remote real backend.
    throw new Error(`Unhandled browser test API request: ${route.request().method()} ${path}`);
  });
});

const corsHeaders = {
  'access-control-allow-origin': 'http://127.0.0.1:3100',
  'access-control-allow-methods': 'GET, POST, PATCH, DELETE, OPTIONS',
  'access-control-allow-headers': 'authorization, content-type',
};

test('patient opens the published plan and returns to the list', async ({ page }) => {
  await page.goto('/portal?view=plans');
  await page.getByRole('button', { name: 'Zobacz plan', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`content=${ids.plan}`));
  await expect(page.getByText('Publiczny cel planu')).toBeVisible();
  await expect(page.getByText(/udostępniona wersja 1/)).toBeVisible();
  await expect(page.getByText('Plan testowy - wersja 2', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Wróć do listy', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Zobacz plan', exact: true })).toBeVisible();
});

test('summary view does not render extra internal record fields', async ({ page }) => {
  await page.goto(`/portal?view=summaries&content=${ids.record}`);
  await expect(page.getByText('PUBLIC_SUMMARY_1', { exact: true })).toBeVisible();
  await expect(page.getByText('PRIVATE_INTERVIEW', { exact: true })).toHaveCount(0);
  await expect(page.getByText('PRIVATE_RECOMMENDATIONS', { exact: true })).toHaveCount(0);
  const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflows).toBe(false);
});

test('revoked response clears an already displayed plan', async ({ page }) => {
  await page.goto(`/portal?view=plans&content=${ids.plan}`);
  await expect(page.getByText('Publiczny cel planu')).toBeVisible();
  await page.route(`**/api/patient-portal/therapy-plans/${ids.plan}`, (route) =>
    route.fulfill({
      status: 404,
      contentType: 'application/json',
      headers: corsHeaders,
      body: JSON.stringify({ detail: 'PRIVATE_DETAIL' }),
    }),
  );
  await page.getByRole('button', { name: 'Odśwież plan', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Treść jest niedostępna' })).toBeVisible();
  await expect(page.getByText('Publiczny cel planu')).toHaveCount(0);
  await expect(page.getByText('PRIVATE_DETAIL', { exact: true })).toHaveCount(0);
});
