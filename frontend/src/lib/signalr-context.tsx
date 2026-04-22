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
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useAuthStore } from '@/stores/auth-store';

type HubName = 'chat' | 'notifications';

interface SignalRContextValue {
  /** Subskrybcja eventu z huba. Zwraca funkcję cleanup. */
  on: <T = unknown>(hub: HubName, event: string, handler: (payload: T) => void) => () => void;
  /** Wywołanie metody na hubie (server method). */
  invoke: <T = void>(hub: HubName, method: string, ...args: unknown[]) => Promise<T>;
  /** Stan połączeń — przydatne do wyświetlania "connecting..." w UI. */
  isConnected: Record<HubName, boolean>;
}

const SignalRContext = createContext<SignalRContextValue | null>(null);

export function SignalRProvider({ children }: { children: ReactNode }) {
  const { accessToken, isAuthenticated } = useAuthStore();
  const connections = useRef<Partial<Record<HubName, HubConnection>>>({});
  const [isConnected, setIsConnected] = useState<Record<HubName, boolean>>({
    chat: false,
    notifications: false,
  });

  const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

  // Tworzenie i zarządzanie połączeniami SignalR.
  // Jedno połączenie per hub na całą aplikację - żeby nie mnożyć WebSocket przy każdym komponencie.
  useEffect(() => {
    if (!isAuthenticated || !accessToken) {
      // Zamknij wszystkie połączenia przy wylogowaniu
      Object.entries(connections.current).forEach(([name, conn]) => {
        conn?.stop();
        delete connections.current[name as HubName];
      });
      setIsConnected({ chat: false, notifications: false });
      return;
    }

    const hubs: HubName[] = ['chat', 'notifications'];

    hubs.forEach((hub) => {
      // Nie twórz jeśli już istnieje i działa
      if (connections.current[hub]?.state === HubConnectionState.Connected) return;

      const conn = new HubConnectionBuilder()
        .withUrl(`${apiUrl}/hubs/${hub}`, {
          accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (ctx) =>
            ctx.previousRetryCount < 3 ? 2000 : ctx.previousRetryCount < 6 ? 5000 : 30_000,
        })
        .configureLogging(LogLevel.Warning)
        .build();

      conn.onreconnected(() => setIsConnected((s) => ({ ...s, [hub]: true })));
      conn.onreconnecting(() => setIsConnected((s) => ({ ...s, [hub]: false })));
      conn.onclose(() => setIsConnected((s) => ({ ...s, [hub]: false })));

      conn
        .start()
        .then(() => {
          connections.current[hub] = conn;
          setIsConnected((s) => ({ ...s, [hub]: true }));
        })
        .catch((err) => {
          console.error(`[SignalR] ${hub} failed to connect:`, err);
        });
    });

    return () => {
      Object.entries(connections.current).forEach(([name, conn]) => {
        conn?.stop();
        delete connections.current[name as HubName];
      });
    };
  }, [accessToken, isAuthenticated, apiUrl]);

  const on = useCallback<SignalRContextValue['on']>((hub, event, handler) => {
    const conn = connections.current[hub];
    if (!conn) {
      // Jeszcze nie połączony - zwróć no-op
      return () => {};
    }
    conn.on(event, handler as (...args: unknown[]) => void);
    return () => conn.off(event, handler as (...args: unknown[]) => void);
  }, []);

  const invoke = useCallback<SignalRContextValue['invoke']>(async (hub, method, ...args) => {
    const conn = connections.current[hub];
    if (!conn || conn.state !== HubConnectionState.Connected) {
      throw new Error(`Hub "${hub}" is not connected`);
    }
    return conn.invoke(method, ...args);
  }, []);

  return (
    <SignalRContext.Provider value={{ on, invoke, isConnected }}>
      {children}
    </SignalRContext.Provider>
  );
}

export function useSignalR() {
  const ctx = useContext(SignalRContext);
  if (!ctx) throw new Error('useSignalR must be used within SignalRProvider');
  return ctx;
}

/**
 * Hook subskrybujący konkretny event — automatyczny cleanup przy unmount.
 * Użycie:
 *   useSignalREvent('chat', 'ReceiveMessage', (msg) => setMessages(m => [...m, msg]));
 */
export function useSignalREvent<T = unknown>(
  hub: HubName,
  event: string,
  handler: (payload: T) => void,
) {
  const { on, isConnected } = useSignalR();

  useEffect(() => {
    if (!isConnected[hub]) return;
    const unsubscribe = on<T>(hub, event, handler);
    return unsubscribe;
    // handler celowo poza deps - user odpowiada za stability (useCallback jeśli chce)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hub, event, isConnected[hub]]);
}
