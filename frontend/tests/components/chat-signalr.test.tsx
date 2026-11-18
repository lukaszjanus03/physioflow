import { act, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SignalRProvider, useSignalR, useSignalREvent } from '@/lib/signalr-context';
import { useAuthStore } from '@/stores/auth-store';
import { ids } from '../fixtures/sharing';

const mock = vi.hoisted(() => ({
  connections: [] as Array<{
    on: ReturnType<typeof vi.fn>;
    off: ReturnType<typeof vi.fn>;
    start: ReturnType<typeof vi.fn>;
    stop: ReturnType<typeof vi.fn>;
    reconnect: () => void;
    disconnect: () => void;
  }>,
  fail: false,
}));
vi.mock('@microsoft/signalr', () => ({
  LogLevel: { None: 6 },
  HubConnectionState: { Connected: 'Connected' },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const connection = {
        state: 'Connected',
        on: vi.fn(),
        off: vi.fn(),
        start: vi.fn(async () => {
          if (mock.fail) throw new Error('PRIVATE_TOKEN_URL');
        }),
        stop: vi.fn(async () => {}),
        reconnect: () => {},
        disconnect: () => {},
        onreconnected: (fn: () => void) => {
          connection.reconnect = fn;
        },
        onreconnecting: (fn: () => void) => {
          connection.disconnect = fn;
        },
        onclose: vi.fn(),
        invoke: vi.fn(),
      };
      mock.connections.push(connection);
      return connection;
    }
  },
}));
const receive = vi.fn();
function Subscriber() {
  useSignalREvent('chat', 'MessageCreated', receive);
  const { isConnected } = useSignalR();
  return <p>{isConnected.chat ? 'connected' : 'disconnected'}</p>;
}
beforeEach(() => {
  mock.connections.length = 0;
  mock.fail = false;
  useAuthStore.setState({
    accessToken: 'synthetic-token',
    userId: ids.patient,
    isAuthenticated: true,
    roles: ['Patient'],
  });
});
describe('shared SignalR lifecycle', () => {
  it('registers event handlers before starting and restores status after reconnect', async () => {
    render(
      <SignalRProvider>
        <Subscriber />
      </SignalRProvider>,
    );
    await screen.findByText('connected');
    const chat = mock.connections[0];
    expect(chat.on.mock.invocationCallOrder[0]).toBeLessThan(chat.start.mock.invocationCallOrder[0]);
    act(() => chat.disconnect());
    expect(screen.getByText('disconnected')).toBeVisible();
    act(() => chat.reconnect());
    expect(screen.getByText('connected')).toBeVisible();
    const callback = chat.on.mock.calls[0][1] as (payload: unknown) => void;
    callback({ conversationId: 'hint' });
    expect(receive).toHaveBeenCalledWith({ conversationId: 'hint' });
  });
  it('stops both old connections when the authenticated account changes', async () => {
    const { unmount } = render(
      <SignalRProvider>
        <Subscriber />
      </SignalRProvider>,
    );
    await screen.findByText('connected');
    const old = [...mock.connections];
    act(() => useAuthStore.setState({ userId: ids.therapist }));
    await waitFor(() => expect(mock.connections).toHaveLength(4));
    expect(old.every((connection) => connection.stop.mock.calls.length === 1)).toBe(true);
    unmount();
    expect(mock.connections.every((connection) => connection.stop.mock.calls.length === 1)).toBe(true);
  });
  it('retries an initial connection failure and cancels timers on unmount', async () => {
    vi.useFakeTimers();
    mock.fail = true;
    const { unmount } = render(
      <SignalRProvider>
        <Subscriber />
      </SignalRProvider>,
    );
    await act(async () => {
      await Promise.resolve();
    });
    expect(mock.connections[0].start).toHaveBeenCalledTimes(1);
    mock.fail = false;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(mock.connections[0].start).toHaveBeenCalledTimes(2);
    expect(screen.getByText('connected')).toBeVisible();
    unmount();
    await vi.advanceTimersByTimeAsync(60000);
    expect(mock.connections[0].start).toHaveBeenCalledTimes(2);
  });
});
