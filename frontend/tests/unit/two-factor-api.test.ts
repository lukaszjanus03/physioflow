import { http, HttpResponse } from 'msw';
import { expect, it } from 'vitest';
import { parseProof, twoFactorApi, twoFactorError } from '@/lib/two-factor-api';
import { useAuthStore } from '@/stores/auth-store';
import { apiUrl, server } from '../mocks/server';

it('keeps leading zeroes and sends only one proof field', () => {
  expect(parseProof('totp', '012345')).toEqual({ code: '012345' });
  expect(parseProof('totp', '12345')).toBeNull();
  expect(parseProof('totp', '12x456')).toBeNull();
  const recovery = 'a'.repeat(32);
  expect(parseProof('recovery', recovery)).toEqual({ recoveryCode: recovery });
  expect(parseProof('recovery', 'g'.repeat(32))).toBeNull();
});

it('uses anonymous verification and preserves a wrong-code response without refresh', async () => {
  useAuthStore.setState({ accessToken: 'existing-access', refreshToken: 'existing-refresh' });
  let authorization = '';
  let body: unknown;
  server.use(
    http.post(apiUrl('/api/auth/2fa/verify'), async ({ request }) => {
      authorization = request.headers.get('Authorization') ?? '';
      body = await request.json();
      return HttpResponse.json({ code: 'two_factor_invalid_code', detail: 'private' }, { status: 401 });
    }),
  );
  const proof = { code: '012345' };
  await expect(
    twoFactorApi.verify('test-challenge', proof, new AbortController().signal),
  ).rejects.toMatchObject({ response: { status: 401 } });
  expect(authorization).toBe('');
  expect(body).toEqual({ challengeToken: 'test-challenge', code: '012345' });
  expect(useAuthStore.getState().refreshToken).toBe('existing-refresh');
});

it('keeps the current session when an authenticated action rejects the proof', async () => {
  useAuthStore.setState({ accessToken: 'existing-access', refreshToken: 'existing-refresh' });
  server.use(
    http.post(apiUrl('/api/auth/2fa/disable'), ({ request }) => {
      expect(request.headers.get('Authorization')).toBe('Bearer existing-access');
      return HttpResponse.json({ code: 'two_factor_invalid_credentials' }, { status: 401 });
    }),
  );
  await expect(
    twoFactorApi.disable('wrong', { code: '123456' }, new AbortController().signal),
  ).rejects.toMatchObject({ response: { status: 401 } });
  expect(useAuthStore.getState().accessToken).toBe('existing-access');
});

it('honors a rate-limit response and never displays raw backend details', () => {
  const problem = twoFactorError({
    isAxiosError: true,
    response: {
      status: 429,
      headers: { 'retry-after': '20' },
      data: { code: 'rate_limit_exceeded', detail: 'SECRET' },
    },
  });
  expect(problem.retrySeconds).toBe(20);
  expect(problem.message).not.toContain('SECRET');
});
