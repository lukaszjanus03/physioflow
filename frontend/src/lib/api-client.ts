import axios, { AxiosError, AxiosRequestConfig } from 'axios';
import { useAuthStore } from '@/stores/auth-store';
import type { TokenResponse } from '@/types/api';

export const apiClient = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000',
  headers: { 'Content-Type': 'application/json' },
});

// --- Request interceptor: wstrzykuje JWT
apiClient.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// --- Response interceptor: 401 -> próba refresh, potem retry
let isRefreshing = false;
let waiters: Array<(token: string | null) => void> = [];

const subscribe = (cb: (token: string | null) => void) => waiters.push(cb);
const notify = (token: string | null) => {
  waiters.forEach((cb) => cb(token));
  waiters = [];
};

apiClient.interceptors.response.use(
  (r) => r,
  async (error: AxiosError) => {
    const original = error.config as AxiosRequestConfig & { _retry?: boolean };

    const code = (error.response?.data as { code?: string } | undefined)?.code;
    // An invalid proof is not an expired session.
    const invalidProof = original?.url?.startsWith('/api/auth/2fa/') &&
      (code === 'two_factor_invalid_code' || code === 'two_factor_invalid_credentials');
    if (error.response?.status !== 401 || original?._retry || invalidProof || original?.signal?.aborted) {
      return Promise.reject(error);
    }

    const { accessToken, refreshToken, setTokens, logout } = useAuthStore.getState();
    const sentToken = error.config?.headers.get('Authorization');
    if (sentToken && sentToken !== `Bearer ${accessToken}`) return Promise.reject(error);
    if (!refreshToken) {
      logout();
      if (typeof window !== 'undefined') window.location.href = '/login';
      return Promise.reject(error);
    }

    if (isRefreshing) {
      // inne requesty czekają na wynik refresh
      return new Promise((resolve, reject) => {
        subscribe((newToken) => {
          if (!newToken) return reject(error);
          original.headers = { ...original.headers, Authorization: `Bearer ${newToken}` };
          original._retry = true;
          resolve(apiClient(original));
        });
      });
    }

    isRefreshing = true;
    try {
      const { data } = await axios.post<TokenResponse>(
        `${apiClient.defaults.baseURL}/api/auth/refresh`,
        { refreshToken },
      );
      if (useAuthStore.getState().refreshToken !== refreshToken) {
        notify(null);
        return Promise.reject(error);
      }
      setTokens(data.accessToken, data.refreshToken);
      notify(data.accessToken);

      original._retry = true;
      original.headers = { ...original.headers, Authorization: `Bearer ${data.accessToken}` };
      return apiClient(original);
    } catch (e) {
      notify(null);
      if (useAuthStore.getState().refreshToken === refreshToken) {
        logout();
        if (typeof window !== 'undefined') window.location.href = '/';
      }
      return Promise.reject(e);
    } finally {
      isRefreshing = false;
    }
  },
);
