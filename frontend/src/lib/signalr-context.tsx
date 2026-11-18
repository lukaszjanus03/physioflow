'use client';

import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from 'react';

import { useAuthStore } from '@/stores/auth-store';

type HubName = 'chat' | 'notifications';
type ConnectionStatus = Record<HubName, boolean>;

interface SignalRContextValue {
  on: <T = unknown>(hub: HubName, event: string, handler: (payload: T) => void) => () => void;

  invoke: <T = void>(hub: HubName, method: string, ...args: unknown[]) => Promise<T>;

  isConnected: ConnectionStatus;
}

const DISCONNECTED: ConnectionStatus = {
  chat: false,
  notifications: false,
};

const HUBS: HubName[] = ['chat', 'notifications'];

function createConnectionStore() {
  const connections: Partial<Record<HubName, HubConnection>> = {};
  const handlers = new Map<HubName, Map<string, Set<(payload: unknown) => void>>>();
  const listeners = new Set<() => void>();

  let snapshot: ConnectionStatus = DISCONNECTED;

  function setConnected(hub: HubName, connected: boolean) {
    if (snapshot[hub] === connected) return;

    snapshot = { ...snapshot, [hub]: connected };
    listeners.forEach((listener) => listener());
  }

  return {
    subscribe(listener: () => void) {
      listeners.add(listener);

      return () => {
        listeners.delete(listener);
      };
    },

    getSnapshot: () => snapshot,

    getServerSnapshot: () => DISCONNECTED,

    getConnection: (hub: HubName) => connections[hub],

    register(hub: HubName, connection: HubConnection) {
      connections[hub] = connection;
      for (const [event, listeners] of handlers.get(hub) ?? []) {
        for (const listener of listeners) connection.on(event, listener);
      }
      setConnected(hub, false);
    },

    on<T>(hub: HubName, event: string, handler: (payload: T) => void) {
      const listener = (payload: unknown) => handler(payload as T);
      const events = handlers.get(hub) ?? new Map<string, Set<(payload: unknown) => void>>();
      const group = events.get(event) ?? new Set<(payload: unknown) => void>();
      group.add(listener);
      events.set(event, group);
      handlers.set(hub, events);
      connections[hub]?.on(event, listener);
      return () => {
        connections[hub]?.off(event, listener);
        group.delete(listener);
        if (!group.size) events.delete(event);
      };
    },

    updateStatus(hub: HubName, connection: HubConnection, connected: boolean) {
      if (connections[hub] !== connection) return;

      setConnected(hub, connected);
    },

    remove(hub: HubName, connection: HubConnection) {
      if (connections[hub] !== connection) return;

      delete connections[hub];
      setConnected(hub, false);
    },
  };
}

const SignalRContext = createContext<SignalRContextValue | null>(null);

export function SignalRProvider({ children }: { children: ReactNode }) {
  const userId = useAuthStore((state) => state.userId);
  const accessToken = useAuthStore((state) => state.accessToken);
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);

  const [store] = useState(createConnectionStore);

  const connectionStatus = useSyncExternalStore(store.subscribe, store.getSnapshot, store.getServerSnapshot);

  const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

  useEffect(() => {
    if (!isAuthenticated || !accessToken) return;

    let disposed = false;

    const createdConnections: Array<{
      hub: HubName;
      connection: HubConnection;
      cancelRetry: () => void;
    }> = [];

    for (const hub of HUBS) {
      const connection = new HubConnectionBuilder()
        .withUrl(`${apiUrl}/hubs/${hub}`, {
          accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (context) =>
            context.previousRetryCount < 3 ? 2000 : context.previousRetryCount < 6 ? 5000 : 30_000,
        })
        .configureLogging(LogLevel.None)
        .build();

      let retry: ReturnType<typeof setTimeout> | undefined;
      let attempts = 0;
      const start = () => {
        if (disposed) return;
        void connection
          .start()
          .then(() => {
            attempts = 0;
            if (!disposed) store.updateStatus(hub, connection, true);
          })
          .catch(() => {
            if (disposed) return;
            store.updateStatus(hub, connection, false);
            retry = setTimeout(start, Math.min(30000, 2000 * 2 ** Math.min(attempts++, 4)));
          });
      };
      createdConnections.push({ hub, connection, cancelRetry: () => clearTimeout(retry) });
      store.register(hub, connection);

      connection.onreconnected(() => {
        if (!disposed) {
          store.updateStatus(hub, connection, true);
        }
      });

      connection.onreconnecting(() => {
        if (!disposed) {
          store.updateStatus(hub, connection, false);
        }
      });

      connection.onclose(() => {
        if (!disposed) {
          store.updateStatus(hub, connection, false);
        }
      });

      start();
    }

    return () => {
      disposed = true;

      for (const { hub, connection, cancelRetry } of createdConnections) {
        cancelRetry();
        store.remove(hub, connection);

        void connection.stop().catch(() => {});
      }
    };
  }, [accessToken, isAuthenticated, userId, apiUrl, store]);

  const on = useCallback(
    <T,>(hub: HubName, event: string, handler: (payload: T) => void): (() => void) => {
      return store.on(hub, event, handler);
    },
    [store],
  );

  const invoke = useCallback(
    async <T = void,>(hub: HubName, method: string, ...args: unknown[]): Promise<T> => {
      const connection = store.getConnection(hub);

      if (!connection || connection.state !== HubConnectionState.Connected) {
        throw new Error(`Hub "${hub}" is not connected`);
      }

      return connection.invoke<T>(method, ...args);
    },
    [store],
  );

  const isConnected = isAuthenticated && accessToken ? connectionStatus : DISCONNECTED;

  return <SignalRContext.Provider value={{ on, invoke, isConnected }}>{children}</SignalRContext.Provider>;
}

export function useSignalR() {
  const context = useContext(SignalRContext);

  if (!context) {
    throw new Error('useSignalR must be used within SignalRProvider');
  }

  return context;
}

export function useSignalREvent<T = unknown>(hub: HubName, event: string, handler: (payload: T) => void) {
  const { on } = useSignalR();

  useEffect(() => {
    return on<T>(hub, event, handler);
  }, [on, hub, event, handler]);
}
