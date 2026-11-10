'use client';

import * as Dialog from '@radix-ui/react-dialog';
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import Link from 'next/link';
import { useState } from 'react';

import AppointmentEditor from '@/components/calendar/AppointmentEditor';
import { Button } from '@/components/ui/button';
import { useCurrentTime } from '@/hooks/use-current-time';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import {
  APPOINTMENT_LABELS,
  appointmentDateTime,
  appointmentTime,
  warsawDateKey,
} from '@/lib/appointment-display';
import { appointmentsApi } from '@/lib/appointments-api';
import type { AppointmentAction } from '@/lib/appointments-api';
import { getCalendarError } from '@/lib/calendar-api';
import type { CalendarSettings } from '@/lib/calendar-api';

export type AppointmentSelection =
  | { kind: 'new'; date: string }
  | { kind: 'existing'; id: string };

const actionLabels: Record<AppointmentAction, string> = {
  cancel: 'Odwołaj wizytę',
  complete: 'Oznacz jako odbytą',
  'no-show': 'Oznacz nieobecność',
};

export default function AppointmentDialog({
  accountId,
  selection,
  settings,
  onClose,
  onSaved,
}: {
  accountId: string;
  selection: AppointmentSelection;
  settings: CalendarSettings;
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const now = useCurrentTime();
  const queryClient = useQueryClient();

  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [action, setAction] =
    useState<AppointmentAction | null>(null);

  const { confirmLeave, discardDialog } = useUnsavedChanges(
    dirty,
    busy,
  );

  const id =
    selection.kind === 'existing' ? selection.id : '';

  const details = useQuery({
    queryKey: [
      'therapist-appointments',
      accountId,
      'details',
      id,
    ],
    queryFn: ({ signal }) =>
      appointmentsApi.details(id, signal),
    enabled: selection.kind === 'existing',
    staleTime: 0,
    refetchOnWindowFocus: !editing && !busy,
  });

  async function refresh() {
    await Promise.all([
      queryClient.invalidateQueries({
        queryKey: ['therapist-appointments', accountId],
      }),
      queryClient.invalidateQueries({
        queryKey: ['therapist-available-slots', accountId],
      }),
    ]);
  }

  const statusMutation = useMutation({
    mutationFn: (nextAction: AppointmentAction) =>
      appointmentsApi.changeStatus(id, nextAction),
    retry: false,
    onMutate: () => setBusy(true),
    onSuccess: () =>
      onSaved('Status wizyty został zaktualizowany.'),
    onSettled: async () => {
      setBusy(false);
      await refresh();
    },
  });

  const appointment = details.data;

  const future = appointment
    ? new Date(appointment.startsAt).getTime() > now
    : false;

  const actionAllowed =
    appointment?.status === 'Scheduled' &&
    (action === 'cancel' ? future : !future);

  function close() {
    confirmLeave(onClose);
  }

  return (
    <>
      <Dialog.Root
        open
        onOpenChange={(open) => {
          if (!open) close();
        }}
      >
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

          <Dialog.Content
            className="appointment-wide-dialog fixed left-1/2 top-1/2 z-50 -translate-x-1/2 -translate-y-1/2 rounded-2xl border bg-background shadow-xl"
            onEscapeKeyDown={(event) => {
              event.preventDefault();
              close();
            }}
            onPointerDownOutside={(event) => {
              event.preventDefault();
              close();
            }}
          >
            <Dialog.Title className="text-3xl">
              {selection.kind === 'new'
                ? 'Nowa wizyta'
                : 'Szczegóły wizyty'}
            </Dialog.Title>

            <Dialog.Description className="mt-2 text-sm text-muted-foreground">
              Godziny prezentujemy w strefie Europe/Warsaw.
            </Dialog.Description>

            <div className="mt-6">
              {selection.kind === 'new' ? (
                <AppointmentEditor
                  accountId={accountId}
                  settings={settings}
                  initialDate={selection.date}
                  now={now}
                  onDirtyChange={setDirty}
                  onBusyChange={setBusy}
                  onRefresh={refresh}
                  onSaved={onSaved}
                />
              ) : details.isPending ? (
                <p role="status">
                  Wczytywanie szczegółów…
                </p>
              ) : details.isError ? (
                <div className="space-y-4">
                  <p
                    role="alert"
                    className="text-sm text-destructive"
                  >
                    {getCalendarError(details.error)}
                  </p>

                  <Button
                    type="button"
                    variant="outline"
                    disabled={details.isFetching}
                    onClick={() => void details.refetch()}
                  >
                    Spróbuj ponownie
                  </Button>
                </div>
              ) : appointment ? (
                <div className="space-y-5">
                  <section className="rounded-xl border bg-primary/5 p-4">
                    <h2 className="text-2xl">
                      {appointment.patientName}
                    </h2>

                    <p className="mt-3 text-sm">
                      {appointmentDateTime(
                        appointment.startsAt,
                      )}
                      {' – '}
                      {appointmentTime(appointment.endsAt)}
                    </p>

                    <p className="mt-2 text-sm text-muted-foreground">
                      {appointment.durationMinutes} minut ·{' '}
                      {APPOINTMENT_LABELS[appointment.status]}
                    </p>

                    <Link
                      href={`/patients/${appointment.patientId}`}
                      className="mt-3 inline-block text-sm text-primary underline"
                    >
                      Otwórz kartę pacjenta
                    </Link>
                  </section>

                  <section className="space-y-3 rounded-xl border border-primary/20 bg-primary/5 p-4">
                    <h2 className="text-2xl">
                      Wiadomość pacjenta
                    </h2>

                    {appointment.patientMessage?.trim() ? (
                      <p className="whitespace-pre-wrap break-words text-sm leading-relaxed">
                        {appointment.patientMessage}
                      </p>
                    ) : (
                      <p className="text-sm text-muted-foreground">
                        Nie dodano wiadomości pacjenta
                        przy rezerwacji.
                      </p>
                    )}
                  </section>

                  {editing &&
                  appointment.status === 'Scheduled' ? (
                    <>
                      <AppointmentEditor
                        accountId={accountId}
                        settings={settings}
                        appointment={appointment}
                        initialDate={warsawDateKey(
                          appointment.startsAt,
                        )}
                        now={now}
                        onDirtyChange={setDirty}
                        onBusyChange={setBusy}
                        onRefresh={refresh}
                        onSaved={onSaved}
                      />

                      <Button
                        type="button"
                        variant="outline"
                        disabled={busy}
                        onClick={() =>
                          confirmLeave(() => {
                            setDirty(false);
                            setEditing(false);
                          })
                        }
                      >
                        Wróć do szczegółów
                      </Button>
                    </>
                  ) : action ? (
                    <section className="space-y-4 rounded-xl border p-4">
                      <h2 className="text-2xl">
                        {actionLabels[action]}?
                      </h2>

                      <p className="text-sm text-muted-foreground">
                        {action === 'cancel'
                          ? 'Termin zostanie zwolniony. Wizyta pozostanie w historii.'
                          : 'Zmiana statusu zostanie zapisana w historii wizyty.'}
                      </p>

                      {!actionAllowed && (
                        <p
                          role="alert"
                          className="text-sm text-destructive"
                        >
                          Operacja nie jest już dostępna
                          dla aktualnego czasu lub statusu
                          wizyty.
                        </p>
                      )}

                      {statusMutation.isError && (
                        <p
                          role="alert"
                          className="text-sm text-destructive"
                        >
                          {getCalendarError(
                            statusMutation.error,
                          )}
                        </p>
                      )}

                      <div className="flex flex-wrap gap-3">
                        <Button
                          type="button"
                          disabled={
                            statusMutation.isPending ||
                            !actionAllowed
                          }
                          onClick={() =>
                            statusMutation.mutate(action)
                          }
                        >
                          {statusMutation.isPending
                            ? 'Zapisywanie…'
                            : 'Potwierdź'}
                        </Button>

                        <Button
                          type="button"
                          variant="outline"
                          disabled={statusMutation.isPending}
                          onClick={() => setAction(null)}
                        >
                          Anuluj
                        </Button>
                      </div>
                    </section>
                  ) : (
                    <>
                      <div className="space-y-4 text-sm">
                        <div>
                          <p className="font-medium">
                            Powód wizyty
                          </p>
                          <p className="mt-2 whitespace-pre-wrap break-words text-muted-foreground">
                            {appointment.reason ||
                              'Nie podano.'}
                          </p>
                        </div>

                        <div>
                          <p className="font-medium">
                            Notatka terapeuty
                          </p>
                          <p className="mt-2 whitespace-pre-wrap break-words text-muted-foreground">
                            {appointment.notes ||
                              'Brak notatki.'}
                          </p>
                        </div>
                      </div>

                      {appointment.status ===
                        'Scheduled' && (
                        <div className="flex flex-wrap gap-3">
                          {future && (
                            <Button
                              type="button"
                              onClick={() =>
                                setEditing(true)
                              }
                            >
                              Edytuj wizytę
                            </Button>
                          )}

                          {(future
                            ? (['cancel'] as const)
                            : ([
                                'complete',
                                'no-show',
                              ] as const)
                          ).map((nextAction) => (
                            <Button
                              key={nextAction}
                              type="button"
                              variant="outline"
                              onClick={() => {
                                statusMutation.reset();
                                setAction(nextAction);
                              }}
                            >
                              {actionLabels[nextAction]}
                            </Button>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </div>
              ) : null}
            </div>

            <div className="mt-6 border-t pt-4">
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={close}
              >
                Zamknij
              </Button>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>

      {discardDialog}
    </>
  );
}