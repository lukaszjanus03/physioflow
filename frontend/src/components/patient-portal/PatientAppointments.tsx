'use client';

import * as Dialog from '@radix-ui/react-dialog';
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { addDays, format } from 'date-fns';
import { pl } from 'date-fns/locale';
import { useState } from 'react';
import type { FormEvent } from 'react';

import PatientBookingDialog from '@/components/patient-portal/PatientBookingDialog';
import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import { useCurrentTime } from '@/hooks/use-current-time';
import {
  APPOINTMENT_LABELS,
  appointmentDateTime,
  appointmentTime,
  toCalendarDate,
} from '@/lib/appointment-display';
import { isCalendarDate } from '@/lib/calendar-validation';
import {
  patientAppointmentsApi,
  patientAppointmentError,
  patientAppointmentKeys,
} from '@/lib/patient-appointments-api';
import type { PatientAppointment } from '@/lib/patient-appointments-api';

type VisitTab = 'upcoming' | 'history';

type DateRange = {
  from: string;
  to: string;
};

const PAGE_SIZE = 5;

const statusStyles = {
  Scheduled: 'bg-teal-50 text-teal-800',
  Completed: 'bg-blue-50 text-blue-800',
  Cancelled: 'bg-slate-100 text-slate-700',
  NoShow: 'bg-rose-50 text-rose-800',
};

function therapistName(appointment: PatientAppointment) {
  return (
    `${appointment.therapist.firstName} ` +
    appointment.therapist.lastName
  );
}

function defaultRange(
  tab: VisitTab,
  now: number,
): DateRange {
  const today = toCalendarDate(new Date(now));

  return {
    from: format(
      tab === 'history' ? addDays(today, -90) : today,
      'yyyy-MM-dd',
    ),
    to: format(addDays(today, 90), 'yyyy-MM-dd'),
  };
}

function displayDate(value: string) {
  return value.split('-').reverse().join('.');
}

function isUpcoming(
  appointment: PatientAppointment,
  now: number,
) {
  return (
    appointment.status === 'Scheduled' &&
    new Date(appointment.endsAt).getTime() > now
  );
}

function AppointmentInfo({
  appointment,
}: {
  appointment: PatientAppointment;
}) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-2xl">
            {therapistName(appointment)}
          </h2>

          <p className="mt-2 text-sm text-muted-foreground">
            {[
              appointment.therapist.practiceName,
              appointment.therapist.city,
            ]
              .filter(Boolean)
              .join(' · ')}
          </p>
        </div>

        <span
          className={`rounded-full px-3 py-1 text-xs ${
            statusStyles[appointment.status]
          }`}
        >
          {APPOINTMENT_LABELS[appointment.status]}
        </span>
      </div>

      <p className="text-sm font-medium">
        {appointmentDateTime(appointment.startsAt)}
        {' – '}
        {appointmentTime(appointment.endsAt)}
      </p>

      <p className="text-sm text-muted-foreground">
        {appointment.durationMinutes} minut · Europe/Warsaw
      </p>
    </div>
  );
}

export default function PatientAppointments({
  accountId,
  onFindTherapist,
}: {
  accountId: string;
  onFindTherapist: () => void;
}) {
  const now = useCurrentTime();

  const [tab, setTab] = useState<VisitTab>('upcoming');
  const [range, setRange] = useState<DateRange>(() =>
    defaultRange('upcoming', now),
  );
  const [rangeDraft, setRangeDraft] = useState(range);
  const [rangeError, setRangeError] = useState('');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);

  const [booking, setBooking] = useState(false);
  const [selectedId, setSelectedId] =
    useState<string | null>(null);
  const [notice, setNotice] = useState('');

  const query = useQuery({
    queryKey: [
      ...patientAppointmentKeys.all(accountId),
      'list',
      range.from,
      range.to,
    ],
    queryFn: ({ signal }) =>
      patientAppointmentsApi.list(
        range.from,
        range.to,
        signal,
      ),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });

  const items = (query.data ?? [])
    .filter((appointment) =>
      tab === 'upcoming'
        ? isUpcoming(appointment, now)
        : !isUpcoming(appointment, now),
    )
    .sort((a, b) => {
      const difference =
        new Date(a.startsAt).getTime() -
        new Date(b.startsAt).getTime();

      return (
        (tab === 'upcoming' ? difference : -difference) ||
        a.id.localeCompare(b.id)
      );
    });

  const displayedItems = items.slice(0, visibleCount);

  function switchTab(nextTab: VisitTab) {
    const nextRange = defaultRange(nextTab, now);

    setTab(nextTab);
    setRange(nextRange);
    setRangeDraft(nextRange);
    setVisibleCount(PAGE_SIZE);
    setRangeError('');
    setFiltersOpen(false);
    setNotice('');
  }

  function applyRange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setRangeError('');

    if (
      !isCalendarDate(rangeDraft.from) ||
      !isCalendarDate(rangeDraft.to) ||
      rangeDraft.from > rangeDraft.to
    ) {
      setRangeError('Wybierz poprawny zakres dat.');
      return;
    }

    const days =
      (Date.parse(`${rangeDraft.to}T00:00:00Z`) -
        Date.parse(`${rangeDraft.from}T00:00:00Z`)) /
      86_400_000;

    if (days > 366) {
      setRangeError(
        'Różnica dat nie może przekraczać 366 dni.',
      );
      return;
    }

    const unchanged =
      rangeDraft.from === range.from &&
      rangeDraft.to === range.to;

    setRange({ ...rangeDraft });
    setVisibleCount(PAGE_SIZE);
    setFiltersOpen(false);

    if (unchanged) void query.refetch();
  }

  function resetRange() {
    const nextRange = defaultRange(tab, now);

    const unchanged =
      nextRange.from === range.from &&
      nextRange.to === range.to;

    setRange(nextRange);
    setRangeDraft(nextRange);
    setVisibleCount(PAGE_SIZE);
    setRangeError('');

    if (unchanged) void query.refetch();
  }

  function openBooking() {
    setNotice('');
    setBooking(true);
  }

  return (
    <section className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-4xl">Moje wizyty</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Twoje najbliższe terminy i historia wizyt.
          </p>
        </div>

        <Button type="button" onClick={openBooking}>
          Zarezerwuj wizytę
        </Button>
      </header>

      {notice && (
        <p
          role="status"
          className="rounded-xl bg-primary/10 p-4 text-sm text-primary"
        >
          {notice}
        </p>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div
          role="group"
          aria-label="Rodzaj wizyt"
          className="inline-flex gap-1 rounded-xl border bg-card p-1"
        >
          <Button
            type="button"
            variant={tab === 'upcoming' ? 'default' : 'ghost'}
            aria-pressed={tab === 'upcoming'}
            onClick={() => switchTab('upcoming')}
          >
            Nadchodzące
          </Button>

          <Button
            type="button"
            variant={tab === 'history' ? 'default' : 'ghost'}
            aria-pressed={tab === 'history'}
            onClick={() => switchTab('history')}
          >
            Historia
          </Button>
        </div>

        <div className="flex gap-2">
          <Button
            type="button"
            variant="outline"
            aria-expanded={filtersOpen}
            aria-controls="patient-appointment-filters"
            onClick={() => setFiltersOpen((current) => !current)}
          >
            {filtersOpen ? 'Ukryj filtry' : 'Filtry'}
          </Button>

          <Button
            type="button"
            variant="outline"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            {query.isFetching ? 'Odświeżanie…' : 'Odśwież'}
          </Button>
        </div>
      </div>

      <div
        id="patient-appointment-filters"
        hidden={!filtersOpen}
        className="rounded-xl border bg-card p-4 sm:p-5"
      >
        <form noValidate onSubmit={applyRange} className="space-y-4">
          <div>
            <h2 className="text-2xl">Zakres dat</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Filtr dotyczy aktualnie wybranej zakładki.
              Różnica dat może wynosić maksymalnie 366 dni.
            </p>
          </div>

          <div className="grid items-end gap-4 sm:grid-cols-[1fr_1fr_auto]">
            <div className="space-y-2">
              <Label htmlFor="appointments-from">Od</Label>
              <Input
                id="appointments-from"
                type="date"
                value={rangeDraft.from}
                onChange={(event) =>
                  setRangeDraft({
                    ...rangeDraft,
                    from: event.target.value,
                  })
                }
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="appointments-to">Do</Label>
              <Input
                id="appointments-to"
                type="date"
                value={rangeDraft.to}
                onChange={(event) =>
                  setRangeDraft({
                    ...rangeDraft,
                    to: event.target.value,
                  })
                }
              />
            </div>

            <Button type="submit">Zastosuj</Button>
          </div>

          {rangeError && (
            <p role="alert" className="text-sm text-destructive">
              {rangeError}
            </p>
          )}

          <Button
            type="button"
            variant="outline"
            onClick={resetRange}
          >
            Przywróć domyślny zakres
          </Button>
        </form>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
        <p>
          {displayDate(range.from)} – {displayDate(range.to)}
          {' · '}Europe/Warsaw
        </p>

        <p>
          Starsze lub dalsze terminy znajdziesz przez „Filtry”.
        </p>
      </div>

      {tab === 'history' && (
        <p className="text-sm text-muted-foreground">
          Odbyte, odwołane i minione terminy. Odwołane wizyty
          znajdziesz tutaj również wtedy, gdy ich data jest
          jeszcze w przyszłości.
        </p>
      )}

      {query.isPending ? (
        <p role="status" className="py-6 text-sm text-muted-foreground">
          Wczytywanie wizyt…
        </p>
      ) : query.isError ? (
        <div className="space-y-4 rounded-xl border bg-card p-5">
          <p role="alert" className="text-sm text-destructive">
            {patientAppointmentError(query.error)}
          </p>

          <Button
            type="button"
            variant="outline"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </div>
      ) : items.length === 0 ? (
        <div className="rounded-2xl border border-dashed bg-card px-5 py-10 text-center">
          <h2 className="text-3xl">
            {tab === 'upcoming'
              ? 'Brak nadchodzących wizyt'
              : 'Brak wizyt w historii'}
          </h2>

          <p className="mx-auto mt-3 max-w-md text-sm leading-relaxed text-muted-foreground">
            {tab === 'upcoming'
              ? 'W wybranym zakresie nie ma aktualnych ani nadchodzących rezerwacji.'
              : 'W wybranym zakresie nie ma zakończonych, minionych ani odwołanych wizyt.'}
          </p>

          <div className="mt-5 flex flex-wrap justify-center gap-3">
            {tab === 'upcoming' && (
              <Button type="button" onClick={openBooking}>
                Zarezerwuj wizytę
              </Button>
            )}

            <Button
              type="button"
              variant="outline"
              onClick={() => setFiltersOpen(true)}
            >
              Zmień zakres dat
            </Button>
          </div>
        </div>
      ) : (
        <>
          <div className="space-y-3" aria-busy={query.isFetching}>
            {displayedItems.map((appointment, index) => {
              const appointmentDate = toCalendarDate(
                appointment.startsAt,
              );

              const highlighted =
                tab === 'upcoming' && index === 0;

              return (
                <article
                  key={appointment.id}
                  className={`flex flex-col gap-4 rounded-2xl border p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5 ${
                    highlighted
                      ? 'border-primary/30 bg-primary/5'
                      : 'bg-card'
                  }`}
                >
                  <div className="flex min-w-0 items-start gap-4">
                    <div
                      aria-hidden="true"
                      className={`flex h-16 w-16 shrink-0 flex-col items-center justify-center rounded-xl ${
                        highlighted
                          ? 'bg-primary text-primary-foreground'
                          : 'border bg-muted/40'
                      }`}
                    >
                      <span className="text-xl font-semibold tabular-nums">
                        {format(appointmentDate, 'dd')}
                      </span>
                      <span className="mt-1 text-xs capitalize">
                        {format(appointmentDate, 'MMM', {
                          locale: pl,
                        })}
                      </span>
                    </div>

                    <div className="min-w-0">
                      {highlighted && (
                        <p className="mb-1 text-[10px] font-semibold uppercase tracking-widest text-primary">
                          Najbliższy termin
                        </p>
                      )}

                      <h2 className="break-words text-2xl">
                        {therapistName(appointment)}
                      </h2>

                      <p className="mt-1 text-sm">
                        {appointmentDateTime(appointment.startsAt)}
                        {' – '}
                        {appointmentTime(appointment.endsAt)}
                      </p>

                      <p className="mt-1 text-xs text-muted-foreground">
                        {appointment.durationMinutes} minut
                        {[
                          appointment.therapist.practiceName,
                          appointment.therapist.city,
                        ]
                          .filter(Boolean)
                          .map((value) => ` · ${value}`)
                          .join('')}
                      </p>
                    </div>
                  </div>

                  <div className="flex shrink-0 flex-wrap items-center gap-3 sm:flex-col sm:items-end">
                    <span
                      className={`rounded-full px-3 py-1 text-xs ${
                        statusStyles[appointment.status]
                      }`}
                    >
                      {APPOINTMENT_LABELS[appointment.status]}
                    </span>

                    <Button
                      type="button"
                      variant="outline"
                      onClick={() =>
                        setSelectedId(appointment.id)
                      }
                    >
                      Szczegóły wizyty
                    </Button>
                  </div>
                </article>
              );
            })}
          </div>

          <div className="flex flex-col items-center gap-3 pt-2">
            <p className="text-xs text-muted-foreground">
              Pokazano {displayedItems.length} z {items.length} wizyt
              w wybranym zakresie.
            </p>

            {displayedItems.length < items.length && (
              <Button
                type="button"
                variant="outline"
                disabled={query.isFetching}
                onClick={() =>
                  setVisibleCount((current) => current + PAGE_SIZE)
                }
              >
                Pokaż więcej
              </Button>
            )}
          </div>
        </>
      )}

      {booking && (
        <PatientBookingDialog
          accountId={accountId}
          onClose={() => setBooking(false)}
          onFindTherapist={onFindTherapist}
          onSaved={() => {
            setBooking(false);
            switchTab('upcoming');
            setNotice('Wizyta została zarezerwowana.');
          }}
        />
      )}

      {selectedId && (
        <PatientAppointmentDetails
          key={selectedId}
          accountId={accountId}
          appointmentId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}
    </section>
  );
}

export function PatientAppointmentDetails({
  accountId,
  appointmentId,
  onClose,
}: {
  accountId: string;
  appointmentId: string;
  onClose: () => void;
}) {
  const now = useCurrentTime();
  const queryClient = useQueryClient();

  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);

  const query = useQuery({
    queryKey: patientAppointmentKeys.details(
      accountId,
      appointmentId,
    ),
    queryFn: ({ signal }) =>
      patientAppointmentsApi.details(appointmentId, signal),
    staleTime: 0,
    refetchOnWindowFocus: !busy,
  });

  const mutation = useMutation({
    mutationFn: () =>
      patientAppointmentsApi.cancel(appointmentId),
    retry: false,
    onMutate: () => setBusy(true),
    onSuccess: (data) => {
      queryClient.setQueryData(
        patientAppointmentKeys.details(
          accountId,
          appointmentId,
        ),
        data,
      );
      setConfirming(false);
    },
    onSettled: async () => {
      try {
        await Promise.all([
          queryClient.invalidateQueries({
            queryKey: patientAppointmentKeys.all(accountId),
          }),
          queryClient.invalidateQueries({
            queryKey: ['therapist-appointments'],
          }),
        ]);
      } finally {
        setBusy(false);
      }
    },
  });

  const appointment = query.data;

  const canCancel = Boolean(
    appointment &&
      appointment.status === 'Scheduled' &&
      appointment.canCancel &&
      now <=
        new Date(
          appointment.cancellationDeadline,
        ).getTime(),
  );

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open && !busy) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 max-h-[90dvh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border bg-background p-6 shadow-xl"
          onEscapeKeyDown={(event) => {
            if (busy) event.preventDefault();
          }}
          onPointerDownOutside={(event) => {
            if (busy) event.preventDefault();
          }}
        >
          <Dialog.Title className="text-3xl">
            Twoja wizyta
          </Dialog.Title>

          <Dialog.Description className="mt-2 text-sm text-muted-foreground">
            Szczegóły rezerwacji i możliwość odwołania.
          </Dialog.Description>

          <div className="mt-6 space-y-5">
            {query.isPending ? (
              <p role="status">
                Wczytywanie szczegółów…
              </p>
            ) : query.isError ? (
              <div className="space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {patientAppointmentError(query.error)}
                </p>

                <Button
                  type="button"
                  variant="outline"
                  onClick={() => void query.refetch()}
                >
                  Spróbuj ponownie
                </Button>
              </div>
            ) : appointment ? (
              <>
                <AppointmentInfo appointment={appointment} />

                <section className="space-y-3 rounded-xl border border-primary/20 bg-primary/5 p-4">
                  <h2 className="text-2xl">
                    Twoja wiadomość do terapeuty
                  </h2>

                  {appointment.patientMessage?.trim() ? (
                    <p className="whitespace-pre-wrap break-words text-sm leading-relaxed">
                      {appointment.patientMessage}
                    </p>
                  ) : (
                    <p className="text-sm text-muted-foreground">
                      Nie dodano wiadomości podczas rezerwacji.
                    </p>
                  )}
                </section>

                {appointment.therapist.phoneNumber && (
                  <p className="text-sm">
                    Telefon do gabinetu:{' '}
                    <a
                      href={`tel:${appointment.therapist.phoneNumber.replace(
                        /[^\d+]/g,
                        '',
                      )}`}
                      className="text-primary underline"
                    >
                      {appointment.therapist.phoneNumber}
                    </a>
                  </p>
                )}

                {appointment.status === 'Scheduled' && (
                  <aside className="rounded-xl border bg-muted/30 p-4 text-sm leading-relaxed">
                    <p>
                      Samodzielne odwołanie najpóźniej do:{' '}
                      <strong>
                        {appointmentDateTime(
                          appointment.cancellationDeadline,
                        )}
                      </strong>
                      .
                    </p>

                    {!canCancel && (
                      <p className="mt-3">
                        Odwołanie online jest niedostępne.
                        W razie potrzeby skontaktuj się
                        z gabinetem.
                      </p>
                    )}
                  </aside>
                )}

                {mutation.isSuccess && (
                  <p role="status" className="text-sm text-primary">
                    Wizyta została odwołana.
                  </p>
                )}

                {confirming ? (
                  <section className="space-y-4 rounded-xl border p-4">
                    <h2 className="text-2xl">
                      Odwołać wizytę?
                    </h2>

                    <p className="text-sm text-muted-foreground">
                      Termin zostanie zwolniony.
                      Wizyta pozostanie w historii.
                    </p>

                    {!canCancel && (
                      <p role="alert" className="text-sm text-destructive">
                        Możliwość odwołania wygasła lub
                        status wizyty się zmienił.
                      </p>
                    )}

                    {mutation.isError && (
                      <p role="alert" className="text-sm text-destructive">
                        {patientAppointmentError(mutation.error)}
                      </p>
                    )}

                    <div className="flex flex-wrap gap-3">
                      <Button
                        type="button"
                        variant="destructive"
                        disabled={!canCancel || mutation.isPending}
                        onClick={() => mutation.mutate()}
                      >
                        {mutation.isPending
                          ? 'Odwoływanie…'
                          : 'Potwierdź odwołanie'}
                      </Button>

                      <Button
                        type="button"
                        variant="outline"
                        disabled={mutation.isPending}
                        onClick={() => setConfirming(false)}
                      >
                        Wróć
                      </Button>
                    </div>
                  </section>
                ) : canCancel ? (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => {
                      mutation.reset();
                      setConfirming(true);
                    }}
                  >
                    Odwołaj wizytę
                  </Button>
                ) : null}
              </>
            ) : null}
          </div>

          <div className="mt-6 border-t pt-4">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={onClose}
            >
              Zamknij
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function PatientNextAppointment({
  accountId,
  onDetails,
  onAll,
}: {
  accountId: string;
  onDetails: (id: string) => void;
  onAll: () => void;
}) {
  const query = useQuery({
    queryKey: patientAppointmentKeys.summary(accountId),
    queryFn: ({ signal }) =>
      patientAppointmentsApi.summary(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });

  return (
    <section className="space-y-5 rounded-2xl border bg-card p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-3xl">
          Najbliższa wizyta
        </h2>

        <Button
          type="button"
          variant="outline"
          onClick={onAll}
        >
          Wszystkie wizyty
        </Button>
      </div>

      {query.isPending ? (
        <p role="status">Wczytywanie wizyty…</p>
      ) : query.isError ? (
        <div className="space-y-3">
          <p role="alert" className="text-sm text-destructive">
            {patientAppointmentError(query.error)}
          </p>

          <Button
            type="button"
            variant="outline"
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </div>
      ) : query.data.nextAppointment ? (
        <>
          <AppointmentInfo
            appointment={query.data.nextAppointment}
          />

          <Button
            type="button"
            onClick={() =>
              onDetails(query.data.nextAppointment!.id)
            }
          >
            Zobacz szczegóły
          </Button>
        </>
      ) : (
        <p className="text-sm text-muted-foreground">
          Nie masz nadchodzących zaplanowanych wizyt.
        </p>
      )}
    </section>
  );
}