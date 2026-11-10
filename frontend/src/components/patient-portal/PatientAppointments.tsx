'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { addDays, format } from 'date-fns';
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

const statusStyles = {
  Scheduled: 'bg-teal-50 text-teal-800',
  Completed: 'bg-blue-50 text-blue-800',
  Cancelled: 'bg-slate-100 text-slate-700',
  NoShow: 'bg-rose-50 text-rose-800',
};

function therapistName(appointment: PatientAppointment) {
  return `${appointment.therapist.firstName} ${appointment.therapist.lastName}`;
}

function dateRange(past = false) {
  const today = toCalendarDate(new Date());

  return past
    ? {
        from: format(addDays(today, -90), 'yyyy-MM-dd'),
        to: format(today, 'yyyy-MM-dd'),
      }
    : {
        from: format(today, 'yyyy-MM-dd'),
        to: format(addDays(today, 90), 'yyyy-MM-dd'),
      };
}

function AppointmentInfo({ appointment }: { appointment: PatientAppointment }) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-2xl">{therapistName(appointment)}</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            {[
              appointment.therapist.practiceName,
              appointment.therapist.city,
            ].filter(Boolean).join(' · ')}
          </p>
        </div>

        <span className={`rounded-full px-3 py-1 text-xs ${statusStyles[appointment.status]}`}>
          {APPOINTMENT_LABELS[appointment.status]}
        </span>
      </div>

      <p className="text-sm font-medium">
        {appointmentDateTime(appointment.startsAt)}
        {' – '}{appointmentTime(appointment.endsAt)}
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
  const [range, setRange] = useState(() => dateRange());
  const [rangeDraft, setRangeDraft] = useState(range);
  const [rangeError, setRangeError] = useState('');
  const [filter, setFilter] = useState<'all' | 'upcoming' | 'history'>('all');
  const [booking, setBooking] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [notice, setNotice] = useState('');

  const query = useQuery({
    queryKey: [
      ...patientAppointmentKeys.all(accountId),
      'list',
      range.from,
      range.to,
    ],
    queryFn: ({ signal }) =>
      patientAppointmentsApi.list(range.from, range.to, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });

  const items = (query.data ?? []).filter((appointment) => {
    if (filter === 'all') return true;

    const upcoming =
      appointment.status === 'Scheduled' &&
      new Date(appointment.startsAt).getTime() > now;

    return filter === 'upcoming' ? upcoming : !upcoming;
  });

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
        Date.parse(`${rangeDraft.from}T00:00:00Z`)) / 86_400_000;

    if (days > 366) {
      setRangeError('Różnica dat nie może przekraczać 366 dni.');
      return;
    }

    setRange({ ...rangeDraft });
    setFilter('all');

    if (
      rangeDraft.from === range.from &&
      rangeDraft.to === range.to
    ) {
      void query.refetch();
    }
  }

  function quickRange(past: boolean) {
    const next = dateRange(past);
    setRange(next);
    setRangeDraft(next);
    setRangeError('');
    setFilter(past ? 'history' : 'upcoming');
  }

  return (
    <section className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-4xl">Moje wizyty</h1>
          <p className="mt-3 text-sm text-muted-foreground">
            Rezerwacje, historia i szczegóły Twoich wizyt.
          </p>
        </div>
        <Button type="button" onClick={() => { setNotice(''); setBooking(true); }}>
          Zarezerwuj wizytę
        </Button>
      </header>

      {notice && (
        <p role="status" className="rounded-xl bg-primary/10 p-4 text-sm text-primary">
          {notice}
        </p>
      )}

      <form
        noValidate
        onSubmit={applyRange}
        className="space-y-4 rounded-xl border bg-card p-5"
      >
        <div className="grid items-end gap-4 sm:grid-cols-[1fr_1fr_auto]">
          <div className="space-y-2">
            <Label htmlFor="appointments-from">Od</Label>
            <Input
              id="appointments-from"
              type="date"
              value={rangeDraft.from}
              onChange={(event) =>
                setRangeDraft({ ...rangeDraft, from: event.target.value })
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
                setRangeDraft({ ...rangeDraft, to: event.target.value })
              }
            />
          </div>
          <Button type="submit" variant="outline">Pokaż zakres</Button>
        </div>

        {rangeError && (
          <p role="alert" className="text-sm text-destructive">{rangeError}</p>
        )}

        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={() => quickRange(false)}>
            Nadchodzące — 90 dni
          </Button>
          <Button type="button" variant="outline" onClick={() => quickRange(true)}>
            Historia — 90 dni
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
      </form>

      <div className="flex flex-wrap gap-2" aria-label="Filtr wizyt w wybranym zakresie">
        {([
          ['all', 'Wszystkie'],
          ['upcoming', 'Nadchodzące'],
          ['history', 'Historia i odwołane'],
        ] as const).map(([value, label]) => (
          <Button
            key={value}
            type="button"
            variant={filter === value ? 'default' : 'outline'}
            aria-pressed={filter === value}
            onClick={() => setFilter(value)}
          >
            {label}
          </Button>
        ))}
      </div>

      <p className="text-xs text-muted-foreground">
        Pokazujemy wizyty z zakresu {range.from} – {range.to}.
        Starsze lub dalsze terminy znajdziesz po zmianie dat.
      </p>

      {query.isPending ? (
        <p role="status">Wczytywanie wizyt…</p>
      ) : query.isError ? (
        <p role="alert" className="rounded-xl border p-5 text-sm text-destructive">
          {patientAppointmentError(query.error)}
        </p>
      ) : items.length === 0 ? (
        <div className="rounded-xl border border-dashed p-8 text-center">
          <h2 className="text-2xl">Brak wizyt do wyświetlenia</h2>
          <p className="mt-3 text-sm text-muted-foreground">
            Zmień zakres dat, filtr albo zarezerwuj nową wizytę.
          </p>
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {items.map((appointment) => (
            <article key={appointment.id} className="space-y-5 rounded-xl border bg-card p-5">
              <AppointmentInfo appointment={appointment} />
              <Button
                type="button"
                variant="outline"
                onClick={() => setSelectedId(appointment.id)}
              >
                Szczegóły wizyty
              </Button>
            </article>
          ))}
        </div>
      )}

      {booking && (
        <PatientBookingDialog
          accountId={accountId}
          onClose={() => setBooking(false)}
          onFindTherapist={onFindTherapist}
          onSaved={() => {
            setBooking(false);
            quickRange(false);
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
    queryKey: patientAppointmentKeys.details(accountId, appointmentId),
    queryFn: ({ signal }) =>
      patientAppointmentsApi.details(appointmentId, signal),
    staleTime: 0,
    refetchOnWindowFocus: !busy,
  });

  const mutation = useMutation({
    mutationFn: () => patientAppointmentsApi.cancel(appointmentId),
    retry: false,
    onMutate: () => setBusy(true),
    onSuccess: (data) => {
      queryClient.setQueryData(
        patientAppointmentKeys.details(accountId, appointmentId),
        data,
      );
      setConfirming(false);
    },
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: patientAppointmentKeys.all(accountId),
        }),
        queryClient.invalidateQueries({
          queryKey: ['therapist-appointments'],
        }),
      ]);
      setBusy(false);
    },
  });

  const appointment = query.data;
  const canCancel = Boolean(
    appointment &&
    appointment.status === 'Scheduled' &&
    appointment.canCancel &&
    now <= new Date(appointment.cancellationDeadline).getTime(),
  );

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => { if (!open && !busy) onClose(); }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />
        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 max-h-[90dvh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border bg-background p-6 shadow-xl"
          onEscapeKeyDown={(event) => { if (busy) event.preventDefault(); }}
          onPointerDownOutside={(event) => { if (busy) event.preventDefault(); }}
        >
          <Dialog.Title className="text-3xl">Twoja wizyta</Dialog.Title>
          <Dialog.Description className="mt-2 text-sm text-muted-foreground">
            Szczegóły rezerwacji i możliwość odwołania.
          </Dialog.Description>

          <div className="mt-6 space-y-5">
            {query.isPending ? (
              <p role="status">Wczytywanie szczegółów…</p>
            ) : query.isError ? (
              <div className="space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {patientAppointmentError(query.error)}
                </p>
                <Button type="button" variant="outline" onClick={() => void query.refetch()}>
                  Spróbuj ponownie
                </Button>
              </div>
            ) : appointment ? (
              <>
                <AppointmentInfo appointment={appointment} />

                {appointment.therapist.phoneNumber && (
                  <p className="text-sm">
                    Telefon do gabinetu:{' '}
                    <a
                      href={`tel:${appointment.therapist.phoneNumber.replace(/[^\d+]/g, '')}`}
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
                      <strong>{appointmentDateTime(appointment.cancellationDeadline)}</strong>.
                    </p>
                    {!canCancel && (
                      <p className="mt-3">
                        Odwołanie online jest niedostępne. W razie potrzeby
                        skontaktuj się z gabinetem.
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
                    <h2 className="text-2xl">Odwołać wizytę?</h2>
                    <p className="text-sm text-muted-foreground">
                      Termin zostanie zwolniony. Wizyta pozostanie w historii.
                    </p>

                    {!canCancel && (
                      <p role="alert" className="text-sm text-destructive">
                        Możliwość odwołania wygasła lub status wizyty się zmienił.
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
                        {mutation.isPending ? 'Odwoływanie…' : 'Potwierdź odwołanie'}
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
            <Button type="button" variant="outline" disabled={busy} onClick={onClose}>
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
    queryFn: ({ signal }) => patientAppointmentsApi.summary(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });

  return (
    <section className="space-y-5 rounded-2xl border bg-card p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-3xl">Najbliższa wizyta</h2>
        <Button type="button" variant="outline" onClick={onAll}>
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
          <Button type="button" variant="outline" onClick={() => void query.refetch()}>
            Spróbuj ponownie
          </Button>
        </div>
      ) : query.data.nextAppointment ? (
        <>
          <AppointmentInfo appointment={query.data.nextAppointment} />
          <Button
            type="button"
            onClick={() => onDetails(query.data.nextAppointment!.id)}
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