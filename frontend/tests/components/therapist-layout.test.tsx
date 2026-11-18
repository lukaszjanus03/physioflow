import { act, screen } from '@testing-library/react';
import { renderToString } from 'react-dom/server';
import { hydrateRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import TherapistLayout from '@/app/(therapist)/layout';
import { useAuthStore } from '@/stores/auth-store';

const router = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }));
vi.mock('next/navigation', () => ({
  useRouter: () => router,
  usePathname: () => '/patients/test/exercise-journal',
}));
vi.mock('@/components/notifications/NotificationBell', () => ({ default: () => null }));

async function hydrateLayout(check: () => Promise<void> | void) {
  const client = new QueryClient();
  const tree = (
    <QueryClientProvider client={client}>
      <TherapistLayout>
        <p>PROTECTED_JOURNAL_CONTENT</p>
      </TherapistLayout>
    </QueryClientProvider>
  );
  const container = document.createElement('div');
  container.innerHTML = renderToString(tree);
  // Neither the authenticated content nor a redirect may be inferred from SSR state.
  expect(container.textContent).not.toContain('PROTECTED_JOURNAL_CONTENT');
  document.body.append(container);
  let root: Root | undefined;
  try {
    await act(async () => {
      root = hydrateRoot(container, tree);
    });
    await check();
  } finally {
    await act(async () => root?.unmount());
    client.clear();
    container.remove();
  }
}

describe('therapist layout session hydration', () => {
  it('does not redirect a persisted therapist session during hydration', async () => {
    useAuthStore.getState().setUser('therapist-test', 'therapist@example.test', ['Therapist']);
    await hydrateLayout(async () => {
      expect(await screen.findByText('PROTECTED_JOURNAL_CONTENT')).toBeVisible();
      expect(router.replace).not.toHaveBeenCalled();
    });
  });

  it('redirects an anonymous client after hydration without exposing content', async () => {
    await hydrateLayout(() => {
      expect(screen.queryByText('PROTECTED_JOURNAL_CONTENT')).not.toBeInTheDocument();
      expect(router.replace).toHaveBeenCalledWith('/');
    });
  });

  it('does not render therapist content for a patient-only session', async () => {
    useAuthStore.getState().setUser('patient-test', 'patient@example.test', ['Patient']);
    await hydrateLayout(() => {
      expect(screen.queryByText('PROTECTED_JOURNAL_CONTENT')).not.toBeInTheDocument();
      expect(router.replace).toHaveBeenCalledWith('/');
    });
  });
});
