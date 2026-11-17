import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement, ReactNode } from 'react';
import { afterEach } from 'vitest';

const clients = new Set<QueryClient>();
afterEach(() => {
  for (const client of clients) client.clear();
  clients.clear();
});

export function createQueryWrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  });
  clients.add(client);
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}

export function renderWithQuery(ui: ReactElement) {
  return render(ui, { wrapper: createQueryWrapper() });
}
