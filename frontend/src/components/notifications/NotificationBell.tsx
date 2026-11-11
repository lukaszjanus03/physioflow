'use client';

import * as Dialog from '@radix-ui/react-dialog';
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { Bell, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';

import AppointmentDialog from '@/components/calendar/AppointmentDialog';
import { PatientAppointmentDetails } from '@/components/patient-portal/PatientAppointments';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { appointmentDateTime } from '@/lib/appointment-display';
import {
  calendarApi,
  calendarSettingsKey,
} from '@/lib/calendar-api';
import type { CalendarSettings } from '@/lib/calendar-api';
import { withMinimumLoading } from '@/lib/minimum-loading';
import {
  notificationKeys,
  notificationsApi,
} from '@/lib/notifications-api';
import type { NotificationItem } from '@/lib/notifications-api';
import { getApiMessage } from '@/lib/portal-api';
import {
  useSignalR,
  useSignalREvent,
} from '@/lib/signalr-context';
import { useAuthStore } from '@/stores/auth-store';

type Role = 'Patient' | 'Therapist';

type VisitTarget = {
  id: string;
  settings?: CalendarSettings;
};

export default function NotificationBell({ role }: { role: Role }) {
  const { userId, isAuthenticated, roles } = useAuthStore();

  if (!isAuthenticated || !userId || !roles.includes(role)) {
    return null;
  }

  return (
    <AccountNotifications
      key={`${userId}-${role}`}
      accountId={userId}
      role={role}
    />
  );
}

function AccountNotifications({
  accountId,
  role,
}: {
  accountId: string;
  role: Role;
}) {
  const queryClient = useQueryClient();
  const { isConnected } = useSignalR();
  const triggerRef = useRef<HTMLButtonElement>(null);

  const [open, setOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [notice, setNotice] = useState('');
  const [activeVisit, setActiveVisit] = useState<VisitTarget | null>(
    null,
  );

  const count = useQuery({
    queryKey: notificationKeys.count(accountId),
    queryFn: ({ signal }) => notificationsApi.count(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchOnReconnect: true,
    refetchInterval: 30_000,
  });

  const list = useQuery({
    queryKey: notificationKeys.list(accountId, page),
    queryFn: async ({ signal }) => {
      const cached = queryClient.getQueryData(
        notificationKeys.list(accountId, page),
      );

      const [data] = await Promise.all([
        notificationsApi.list(page, signal),
        cached === undefined
          ? new Promise<void>((resolve) => {
              window.setTimeout(resolve, 450);
            })
          : Promise.resolve(),
      ]);

      return data;
    },
    enabled: open,
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchOnReconnect: true,
    refetchInterval: 30_000,
  });

  const refresh = useCallback(async () => {
    await queryClient.invalidateQueries({
      queryKey: notificationKeys.all(accountId),
    });
  }, [accountId, queryClient]);

  const refreshAfterEvent = useCallback(async () => {
    await Promise.all([
      refresh(),
      queryClient.invalidateQueries({
        queryKey: ['patient-appointments', accountId],
      }),
      queryClient.invalidateQueries({
        queryKey: ['therapist-appointments', accountId],
      }),
      queryClient.invalidateQueries({
        queryKey: ['therapist-available-slots', accountId],
      }),
    ]);
  }, [accountId, queryClient, refresh]);

  const onCreated = useCallback(() => {
    void refreshAfterEvent();
  }, [refreshAfterEvent]);

  useSignalREvent<NotificationItem>(
    'notifications',
    'NotificationCreated',
    onCreated,
  );

  useEffect(() => {
    if (isConnected.notifications) {
      void refreshAfterEvent();
    }
  }, [isConnected.notifications, refreshAfterEvent]);

  const openVisit = useMutation({
    mutationFn: (item: NotificationItem): Promise<VisitTarget> =>
      withMinimumLoading<VisitTarget>(async () => {
        if (!item.readAt) {
          await notificationsApi.read(item.id);
        }

        if (role === 'Patient') {
          return {
            id: item.appointmentId,
          };
        }

        const settings = await queryClient.fetchQuery({
          queryKey: calendarSettingsKey(accountId),
          queryFn: ({ signal }) => calendarApi.settings(signal),
          staleTime: 0,
        });

        return {
          id: item.appointmentId,
          settings,
        };
      }),
    retry: false,
    onSuccess: (target) => {
      setOpen(false);
      setActiveVisit(target);
    },
    onSettled: async () => {
      await refresh();
    },
  });

  const markAll = useMutation({
    mutationFn: notificationsApi.readAll,
    retry: false,
    onSuccess: () => {
      setNotice('Oznaczono powiadomienia jako przeczytane.');
    },
    onSettled: async () => {
      await refresh();
    },
  });

  const busy = openVisit.isPending || markAll.isPending;
  const unread = count.data?.count;

  const label = count.isError
    ? 'Powiadomienia. Nie udało się pobrać licznika.'
    : unread === undefined
      ? 'Powiadomienia'
      : `Powiadomienia. Nieprzeczytane: ${unread}.`;

  function closeVisit() {
    setActiveVisit(null);

    window.requestAnimationFrame(() => {
      triggerRef.current?.focus();
    });
  }

  return (
    <>
      <Dialog.Root
        open={open}
        onOpenChange={(next) => {
          if (busy) return;

          setOpen(next);

          if (next) {
            setPage(1);
            setNotice('');
            openVisit.reset();
            markAll.reset();
          }
        }}
      >
        <Dialog.Trigger asChild>
          <Button
            ref={triggerRef}
            type="button"
            variant="outline"
            size="icon"
            aria-label={label}
            className="relative h-11 w-11 shrink-0"
          >
            <Bell aria-hidden="true" className="h-5 w-5" />

            {(count.isError ||
              (unread !== undefined && unread > 0)) && (
              <span
                aria-hidden="true"
                className="absolute -right-2 -top-2 flex min-h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold text-primary-foreground"
              >
                {count.isError
                  ? '!'
                  : unread !== undefined && unread > 99
                    ? '99+'
                    : unread}
              </span>
            )}
          </Button>
        </Dialog.Trigger>

        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

          <Dialog.Content
            className="fixed left-1/2 top-1/2 z-50 flex max-h-[90dvh] w-[calc(100%_-_2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-2xl border bg-background shadow-xl"
            onEscapeKeyDown={(event) => {
              if (busy) event.preventDefault();
            }}
            onPointerDownOutside={(event) => {
              if (busy) event.preventDefault();
            }}
            onCloseAutoFocus={(event) => {
              if (activeVisit) event.preventDefault();
            }}
          >
            <div className="border-b p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <Dialog.Title className="text-3xl">
                    Powiadomienia
                  </Dialog.Title>

                  <Dialog.Description className="mt-2 text-sm text-muted-foreground">
                    Zmiany dotyczące Twoich wizyt. Kliknij
                    powiadomienie, aby otworzyć szczegóły.
                  </Dialog.Description>
                </div>

                <Dialog.Close asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={busy}
                    aria-label="Zamknij powiadomienia"
                  >
                    <X aria-hidden="true" className="h-5 w-5" />
                  </Button>
                </Dialog.Close>
              </div>

              <div className="mt-4 flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={
                    busy || list.isFetching || count.isFetching
                  }
                  onClick={() => void refresh()}
                >
                  Odśwież
                </Button>

                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={
                    busy ||
                    (count.isSuccess && count.data.count === 0)
                  }
                  onClick={() => {
                    setNotice('');
                    markAll.mutate();
                  }}
                >
                  {markAll.isPending
                    ? 'Zapisywanie…'
                    : 'Oznacz wszystkie jako przeczytane'}
                </Button>
              </div>

              {!isConnected.notifications && (
                <p className="mt-3 text-xs text-muted-foreground">
                  Połączenie na żywo jest niedostępne. Dane
                  odświeżamy co 30 sekund i po powrocie do
                  aplikacji.
                </p>
              )}

              {count.isError && (
                <p
                  role="alert"
                  className="mt-3 text-xs text-destructive"
                >
                  Nie udało się pobrać liczby nieprzeczytanych.
                  Użyj „Odśwież”.
                </p>
              )}

              {notice && (
                <p
                  role="status"
                  className="mt-3 text-sm text-primary"
                >
                  {notice}
                </p>
              )}

              {(openVisit.isError || markAll.isError) && (
                <p
                  role="alert"
                  className="mt-3 text-sm text-destructive"
                >
                  {getApiMessage(
                    openVisit.isError
                      ? openVisit.error
                      : markAll.error,
                  )}
                </p>
              )}

              {openVisit.isPending && (
                <LoadingState
                  title="Otwieranie wizyty…"
                  description="Pobieramy szczegóły wybranej wizyty."
                  className="mt-4 min-h-32 border-0 bg-primary/5"
                />
              )}
            </div>

            <div
              className="min-h-0 overflow-y-auto p-4"
              aria-busy={list.isFetching}
            >
              {list.isPending ? (
                <LoadingState title="Wczytywanie powiadomień…" />
              ) : list.isError ? (
                <p
                  role="alert"
                  className="p-4 text-sm text-destructive"
                >
                  {getApiMessage(list.error)}
                </p>
              ) : list.data.items.length === 0 ? (
                <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">
                  {page === 1
                    ? 'Nie masz jeszcze powiadomień o wizytach.'
                    : 'Na tej stronie nie ma powiadomień. Wróć do poprzedniej strony.'}
                </p>
              ) : (
                <ul className="space-y-3">
                  {list.data.items.map((item) => (
                    <li key={item.id}>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => {
                          setNotice('');
                          openVisit.mutate(item);
                        }}
                        className={`w-full rounded-xl border p-4 text-left transition-colors hover:border-primary/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-60 ${
                          !item.readAt
                            ? 'border-primary/25 bg-primary/5'
                            : 'bg-card'
                        }`}
                      >
                        <span className="flex items-start justify-between gap-3">
                          <span className="text-sm font-semibold">
                            {item.title}
                          </span>

                          {!item.readAt && (
                            <span className="shrink-0 rounded-full bg-primary/10 px-2 py-1 text-[10px] font-medium text-primary">
                              Nowe
                            </span>
                          )}
                        </span>

                        <span className="mt-2 block whitespace-pre-wrap break-words text-sm text-muted-foreground">
                          {item.body}
                        </span>

                        <span className="mt-3 block text-xs text-muted-foreground">
                          {appointmentDateTime(item.createdAt)}
                        </span>

                        <span className="mt-3 block text-xs font-medium text-primary">
                          Otwórz wizytę
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3 border-t p-4">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={busy || list.isFetching || page <= 1}
                onClick={() => setPage((current) => current - 1)}
              >
                Poprzednia
              </Button>

              <span className="text-xs text-muted-foreground">
                Strona {page}
                {list.isSuccess
                  ? ` z ${Math.max(1, list.data.totalPages)}`
                  : ''}
              </span>

              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={
                  busy || list.isFetching || !list.data?.hasNext
                }
                onClick={() => setPage((current) => current + 1)}
              >
                Następna
              </Button>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>

      {activeVisit && role === 'Patient' && (
        <PatientAppointmentDetails
          key={activeVisit.id}
          accountId={accountId}
          appointmentId={activeVisit.id}
          onClose={closeVisit}
        />
      )}

      {activeVisit?.settings && role === 'Therapist' && (
        <AppointmentDialog
          key={activeVisit.id}
          accountId={accountId}
          selection={{
            kind: 'existing',
            id: activeVisit.id,
          }}
          settings={activeVisit.settings}
          onClose={closeVisit}
          onSaved={(message) => {
            closeVisit();
            toast.success(message);
          }}
        />
      )}
    </>
  );
}