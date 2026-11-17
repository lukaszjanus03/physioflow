import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest';
import { server } from './mocks/server';
import { useAuthStore } from '@/stores/auth-store';

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
beforeEach(() => {
  localStorage.clear();
  useAuthStore.setState({
    accessToken: null,
    refreshToken: null,
    userId: null,
    email: null,
    roles: [],
    isAuthenticated: false,
  });
});
afterEach(() => {
  cleanup();
  server.resetHandlers();
  vi.useRealTimers();
  localStorage.clear();
});
afterAll(() => server.close());

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});
