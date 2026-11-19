import { expect, test, type Page } from '@playwright/test';
import { ids, pageOf } from '../tests/fixtures/sharing';

const root = '/api/auth/2fa';
const recoveryCode = 'a'.repeat(32);
const challengeToken = 'synthetic-challenge';
const secret = 'JBSWY3DPEHPK3PXP';
type Mode = 'challenge' | 'prompt' | 'plain';
type Role = 'Patient' | 'Therapist';

async function fakeApi(page: Page, options: { mode: Mode; role: Role; enabled?: boolean }) {
  const { role } = options;
  const account = role === 'Patient' ? ids.patient : ids.therapist;
  const session = {
    accessToken:
      'synthetic.' +
      Buffer.from(JSON.stringify({ sub: account, role, email: 'e2e@example.test' })).toString('base64url') +
      '.signature',
    refreshToken: 'synthetic-refresh',
    expiresAt: '2099-01-01T00:00:00Z',
  };
  const state = {
    enabled: options.enabled ?? false,
    prompt: options.mode === 'prompt',
    refresh: 0,
    verify: [] as unknown[],
    secured: [] as unknown[],
  };
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.startsWith('/hubs/')) return route.abort();
    if (!path.startsWith('/api/'))
      return url.origin === 'http://127.0.0.1:3100' ? route.continue() : route.abort();
    const headers = {
      'access-control-allow-origin': 'http://127.0.0.1:3100',
      'access-control-allow-methods': 'GET, POST, OPTIONS',
      'access-control-allow-headers': 'authorization, content-type',
    };
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    const send = (json: unknown, status = 200) => route.fulfill({ json, status, headers });
    if (path === '/api/auth/login') {
      if (options.mode === 'challenge')
        return send({
          requiresTwoFactor: true,
          challengeToken,
          expiresAt: new Date(Date.now() + 60_000).toISOString(),
        });
      return send({ ...session, shouldPromptTwoFactor: state.prompt });
    }
    if (path === '/api/auth/refresh') {
      state.refresh++;
      return send(session);
    }
    if (path === root + '/verify') {
      expect(request.headers()['authorization']).toBeUndefined();
      const body = request.postDataJSON();
      state.verify.push(body);
      if (body.code === '000000') return send({ code: 'two_factor_invalid_code', detail: 'private' }, 401);
      return send(session);
    }
    if (path.startsWith(root + '/')) {
      expect(request.headers()['authorization']).toBe(`Bearer ${session.accessToken}`);
      if (path === root + '/status')
        return send({
          enabled: state.enabled,
          setupPending: false,
          recoveryCodesRemaining: state.enabled ? 10 : 0,
        });
      if (path === root + '/setup')
        return send({
          sharedKey: secret,
          authenticatorUri: `otpauth://totp/PhysioFlow:e2e?secret=${secret}&issuer=PhysioFlow`,
          expiresAt: new Date(Date.now() + 60_000).toISOString(),
        });
      if (path === root + '/confirm') {
        state.enabled = true;
        state.prompt = false;
        expect(request.postDataJSON()).toEqual({ code: '012345' });
        return send({ recoveryCodes: [recoveryCode] });
      }
      if (path === root + '/recovery-codes/regenerate' || path === root + '/disable') {
        const body = request.postDataJSON();
        state.secured.push(body);
        if (body.password === 'wrong') return send({ code: 'two_factor_invalid_credentials' }, 401);
        if (path.endsWith('/disable')) {
          state.enabled = false;
          return route.fulfill({ status: 204, headers });
        }
        return send({ recoveryCodes: [recoveryCode] });
      }
    }
    if (path === '/api/chat/conversations' || path === '/api/patients') return send(pageOf([]));
    if (path === '/api/appointments/summary') return send({ upcomingAppointments: [] });
    if (path === '/api/analytics/dashboard')
      return send({
        from: '2026-11-16',
        to: '2026-11-22',
        timeZone: 'Europe/Warsaw',
        activePatientCount: 0,
        appointments: { total: 0, scheduled: 0, noShow: 0, cancelled: 0, completed: 0 },
      });
    if (path === '/api/notifications/unread-count') return send({ count: 0 });
    if (path === '/api/patient-accounts/me')
      return send({ id: account, firstName: 'Test', email: 'e2e@example.test', emailConfirmed: true });
    if (path === '/api/patient-accounts/me/cooperations') return send([]);
    if (path === '/api/therapists/me/profile')
      return send({
        id: account,
        firstName: 'Test',
        lastName: 'Test',
        marketplace: { specializations: [] },
        isPublished: false,
        missingFieldsForPublication: [],
      });
    if (path === '/api/therapists/specializations') return send([]);
    if (path.startsWith('/api/therapists/') && path.endsWith('/photo')) return send(null, 404);
    if (path === '/api/patient-portal/appointments') return send(pageOf([]));
    if (path === '/api/patient-portal/appointments/summary') return send({ nextAppointment: null });
    throw new Error(`Unhandled 2FA test request: ${path}`);
  });
  return state;
}

async function passwordStep(page: Page) {
  await page.goto('/login');
  await page.getByLabel('E-mail', { exact: true }).fill('e2e@example.test');
  await page.getByLabel('Hasło', { exact: true }).fill('test-password');
  await page.getByRole('button', { name: 'Zaloguj się', exact: true }).click();
}

const stored = (page: Page) => page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));

test('rejects invalid proof without creating a session and accepts the recovery path', async ({ page }) => {
  const state = await fakeApi(page, { mode: 'challenge', role: 'Therapist' });
  await passwordStep(page);
  await expect(page.getByRole('heading', { name: 'Zweryfikuj logowanie' })).toBeVisible();
  expect(await stored(page)).not.toContain(challengeToken);
  expect(await stored(page)).not.toContain('synthetic-refresh');
  await page.getByLabel('Kod z aplikacji').fill('000000');
  await page.getByRole('button', { name: 'Potwierdź logowanie' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Kod jest nieprawidłowy' })).toBeVisible();
  expect(state.refresh).toBe(0);
  await page.getByRole('button', { name: 'Użyj kodu odzyskiwania' }).click();
  await page.getByLabel('Kod odzyskiwania', { exact: true }).fill(recoveryCode);
  await page.getByRole('button', { name: 'Potwierdź logowanie' }).click();
  await expect(page).toHaveURL('/dashboard');
  expect(state.verify).toEqual([
    { challengeToken, code: '000000' },
    { challengeToken, recoveryCode },
  ]);
});

test('can postpone setup until the next login without dismissing the prompt', async ({ page }) => {
  await fakeApi(page, { mode: 'prompt', role: 'Patient' });
  await passwordStep(page);
  await expect(page.getByRole('heading', { name: 'Zabezpiecz swoje konto' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nie przypominaj', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Przypomnij przy następnym logowaniu' }).click();
  await expect(page).toHaveURL('/portal');
  await expect(page.getByRole('heading', { name: 'Bezpieczeństwo konta' })).toBeVisible();
  await page.evaluate(() => localStorage.removeItem('physioflow-auth'));
  await passwordStep(page);
  await expect(page.getByRole('heading', { name: 'Zabezpiecz swoje konto' })).toBeVisible();
});

test('renders QR locally and keeps recovery codes visible after clearing the session', async ({ page }) => {
  await fakeApi(page, { mode: 'prompt', role: 'Therapist' });
  await passwordStep(page);
  await page.getByRole('button', { name: 'Włącz 2FA' }).click();
  const qr = page.getByRole('img', { name: /Kod QR konfiguracji/ });
  await expect(qr).toBeVisible();
  await expect.poll(() => qr.evaluate((element: HTMLCanvasElement) => element.width)).toBe(220);
  expect(await stored(page)).not.toContain(secret);
  await page.getByLabel('Kod z aplikacji').fill('012345');
  await page.getByRole('button', { name: 'Potwierdź i włącz 2FA' }).click();
  await expect(page.getByRole('heading', { name: 'Zapisz kody odzyskiwania' })).toBeVisible();
  await expect(page.getByText(recoveryCode)).toBeVisible();
  expect(await stored(page)).not.toContain(recoveryCode);
  expect(await stored(page)).not.toContain('synthetic-refresh');
  await expect(page.getByRole('button', { name: 'Wróć do logowania' })).toBeDisabled();
  await page.getByLabel('Zapisano kody w bezpiecznym miejscu').check();
  await page.getByRole('button', { name: 'Wróć do logowania' }).click();
  await expect(page.getByLabel('Hasło', { exact: true })).toBeVisible();
  await expect(page.getByText(recoveryCode)).toHaveCount(0);
});

test('changes account security only after reauthentication and returns to login', async ({ page }) => {
  const state = await fakeApi(page, { mode: 'plain', role: 'Therapist', enabled: true });
  await passwordStep(page);
  await expect(page).toHaveURL('/dashboard');
  await page.goto('/settings');
  await page.getByRole('button', { name: 'Zarządzaj 2FA' }).click();
  await page.getByRole('button', { name: 'Wyłącz 2FA' }).click();
  await page.getByLabel('Aktualne hasło').fill('wrong');
  await page.getByLabel('Kod z aplikacji').fill('012345');
  await page.getByRole('button', { name: 'Potwierdź zmianę' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Nie można potwierdzić tożsamości' })).toBeVisible();
  expect(state.refresh).toBe(0);
  expect(await stored(page)).toContain('synthetic-refresh');
  await page.getByLabel('Aktualne hasło').fill('test-password');
  await page.getByRole('button', { name: 'Użyj kodu odzyskiwania' }).click();
  await page.getByLabel('Kod odzyskiwania', { exact: true }).fill(recoveryCode);
  await page.getByRole('button', { name: 'Potwierdź zmianę' }).click();
  await expect(page).toHaveURL('/login');
  expect(state.secured).toEqual([
    { password: 'wrong', code: '012345' },
    { password: 'test-password', recoveryCode },
  ]);
  expect(await stored(page)).not.toContain('synthetic-refresh');
});
