import axios, { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { TokenResponse } from '@/types/api';

export type LoginSession = TokenResponse & { shouldPromptTwoFactor?: boolean };
export type LoginChallenge = { requiresTwoFactor: true; challengeToken: string; expiresAt: string };
export type LoginResult = LoginSession | LoginChallenge;
export type TwoFactorStatus = { enabled: boolean; setupPending: boolean; recoveryCodesRemaining: number };
export type TwoFactorSetup = { sharedKey: string; authenticatorUri: string; expiresAt: string };
export type TwoFactorProof = { code: string; recoveryCode?: never } | { recoveryCode: string; code?: never };
export type ProofMode = 'totp' | 'recovery';

const anonymous = axios.create({ baseURL: apiClient.defaults.baseURL, timeout: 15_000 });
const root = '/api/auth/2fa';
const config = (signal: AbortSignal) => ({ signal, timeout: 15_000 });

export const twoFactorApi = {
  login: (email: string, password: string, signal?: AbortSignal) =>
    anonymous.post<LoginResult>('/api/auth/login', { email, password }, { signal }).then((r) => r.data),
  verify: (challengeToken: string, proof: TwoFactorProof, signal: AbortSignal) =>
    anonymous
      .post<LoginSession>(root + '/verify', { challengeToken, ...proof }, { signal })
      .then((r) => r.data),
  status: (signal: AbortSignal) =>
    apiClient.get<TwoFactorStatus>(root + '/status', config(signal)).then((r) => r.data),
  setup: (signal: AbortSignal) =>
    apiClient.post<TwoFactorSetup>(root + '/setup', undefined, config(signal)).then((r) => r.data),
  confirm: (code: string, signal: AbortSignal) =>
    apiClient
      .post<{ recoveryCodes: string[] }>(root + '/confirm', { code }, config(signal))
      .then((r) => r.data),
  regenerate: (password: string, proof: TwoFactorProof, signal: AbortSignal) =>
    apiClient
      .post<{
        recoveryCodes: string[];
      }>(root + '/recovery-codes/regenerate', { password, ...proof }, config(signal))
      .then((r) => r.data),
  disable: (password: string, proof: TwoFactorProof, signal: AbortSignal) =>
    apiClient.post<void>(root + '/disable', { password, ...proof }, config(signal)),
};

export function parseProof(mode: ProofMode, value: string): TwoFactorProof | null {
  if (mode === 'totp') return /^[0-9]{6}$/.test(value.trim()) ? { code: value.trim() } : null;
  const recoveryCode = value.trim();
  return /^[0-9a-f]{32}$/i.test(recoveryCode) ? { recoveryCode } : null;
}

export function twoFactorError(error: unknown) {
  const response = isAxiosError<{ code?: string }>(error) ? error.response : undefined;
  const code = response?.data?.code;
  const retryAfter = Number(response?.headers?.['retry-after']);
  const retrySeconds =
    response?.status === 429
      ? Number.isFinite(retryAfter) && retryAfter > 0
        ? Math.min(900, Math.ceil(retryAfter))
        : 300
      : 0;
  const messages: Record<string, string> = {
    two_factor_invalid_code: 'Kod jest nieprawidłowy lub został już użyty. Sprawdź kod i spróbuj ponownie.',
    two_factor_invalid_credentials: 'Nie można potwierdzić tożsamości. Sprawdź hasło lub stan konta.',
    two_factor_invalid_challenge: 'Próba logowania wygasła lub jest już nieaktualna. Wróć do logowania.',
    two_factor_setup_expired: 'Konfiguracja wygasła. Rozpocznij ją ponownie.',
    two_factor_already_enabled: 'Weryfikacja dwuetapowa jest już włączona.',
    two_factor_not_enabled: 'Weryfikacja dwuetapowa jest już wyłączona.',
    concurrent_modification: 'Stan konta zmienił się równolegle. Rozpocznij operację ponownie.',
    two_factor_locked: 'Za dużo prób. Poczekaj przed ponowieniem.',
    rate_limit_exceeded: 'Za dużo żądań. Poczekaj przed ponowieniem.',
    email_confirmation_required: 'Najpierw potwierdź swój adres e-mail.',
    validation_failed: 'Sprawdź poprawność podanych danych.',
  };
  return {
    code,
    retrySeconds,
    message:
      (code && Object.hasOwn(messages, code) ? messages[code] : undefined) ??
      (response?.status === 429
        ? 'Za dużo prób. Poczekaj przed ponowieniem.'
        : response?.status === 401 || response?.status === 403
          ? 'Sesja wygasła lub nie masz dostępu do tej opcji.'
          : 'Nie udało się wykonać operacji. Spróbuj ponownie.'),
  };
}
