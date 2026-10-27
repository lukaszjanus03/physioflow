'use client';

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
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
  on: <T = unknown>(
    hub: HubName,
    event: string,
    handler: (payload: T) => void
  ) => () => void;

  invoke: <T = void>(
    hub: HubName,
    method: string,
    ...args: unknown[]
  ) => Promise<T>;

  isConnected: ConnectionStatus;
}

const DISCONNECTED: ConnectionStatus = {
  chat: false,
  notifications: false,
};

const HUBS: HubName[] = ['chat', 'notifications'];

function createConnectionStore() {
  const connections: Partial<Record<HubName, HubConnection>> = {};
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
      setConnected(hub, false);
    },

    updateStatus(
      hub: HubName,
      connection: HubConnection,
      connected: boolean
    ) {
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
  const accessToken = useAuthStore((state) => state.accessToken);
  const isAuthenticated = useAuthStore(
    (state) => state.isAuthenticated
  );

  const [store] = useState(createConnectionStore);

  const connectionStatus = useSyncExternalStore(
    store.subscribe,
    store.getSnapshot,
    store.getServerSnapshot
  );

  const apiUrl =
    process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

  useEffect(() => {
    if (!isAuthenticated || !accessToken) return;

    let disposed = false;

    const createdConnections: Array<{
      hub: HubName;
      connection: HubConnection;
    }> = [];

    for (const hub of HUBS) {
      const connection = new HubConnectionBuilder()
        .withUrl(`${apiUrl}/hubs/${hub}`, {
          accessTokenFactory: () =>
            useAuthStore.getState().accessToken ?? '',
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (context) =>
            context.previousRetryCount < 3
              ? 2000
              : context.previousRetryCount < 6
                ? 5000
                : 30_000,
        })
        .configureLogging(LogLevel.Warning)
        .build();

      createdConnections.push({ hub, connection });
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

      void connection
        .start()
        .then(() => {
          if (!disposed) {
            store.updateStatus(hub, connection, true);
          }
        })
        .catch((error: unknown) => {
          if (!disposed) {
            store.updateStatus(hub, connection, false);
            console.error(
              `[SignalR] ${hub} failed to connect:`,
              error
            );
          }
        });
    }

    return () => {
      disposed = true;

      for (const { hub, connection } of createdConnections) {
        store.remove(hub, connection);

        void connection.stop().catch((error: unknown) => {
          console.error(
            `[SignalR] ${hub} failed to stop:`,
            error
          );
        });
      }
    };
  }, [accessToken, isAuthenticated, apiUrl, store]);

  const on = useCallback(
    <T,>(
      hub: HubName,
      event: string,
      handler: (payload: T) => void
    ): (() => void) => {
      const connection = store.getConnection(hub);

      if (!connection) return () => {};

      connection.on(event, handler);

      return () => {
        connection.off(event, handler);
      };
    },
    [store]
  );

  const invoke = useCallback(
    async <T = void,>(
      hub: HubName,
      method: string,
      ...args: unknown[]
    ): Promise<T> => {
      const connection = store.getConnection(hub);

      if (
        !connection ||
        connection.state !== HubConnectionState.Connected
      ) {
        throw new Error(`Hub "${hub}" is not connected`);
      }

      return connection.invoke<T>(method, ...args);
    },
    [store]
  );

  const isConnected =
    isAuthenticated && accessToken
      ? connectionStatus
      : DISCONNECTED;

  return (
    <SignalRContext.Provider value={{ on, invoke, isConnected }}>
      {children}
    </SignalRContext.Provider>
  );
}

export function useSignalR() {
  const context = useContext(SignalRContext);

  if (!context) {
    throw new Error(
      'useSignalR must be used within SignalRProvider'
    );
  }

  return context;
}

export function useSignalREvent<T = unknown>(
  hub: HubName,
  event: string,
  handler: (payload: T) => void
) {
  const { on, isConnected } = useSignalR();
  const connected = isConnected[hub];

  useEffect(() => {
    if (!connected) return;

    return on<T>(hub, event, handler);
  }, [on, hub, event, handler, connected]);
}