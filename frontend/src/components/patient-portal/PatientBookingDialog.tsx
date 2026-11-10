'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useRef, useState } from 'react';
import type { FormEvent } from 'react';

import AppointmentSlotSelector from '@/components/calendar/AppointmentSlotSelector';
import { Button } from '@/components/ui/button';
import { Label, Textarea } from '@/components/ui/primitives';
import { useCurrentTime } from '@/hooks/use-current-time';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import {
  appointmentDateTime,
  warsawDateKey,
} from '@/lib/appointment-display';
import { getCalendarFieldErrors } from '@/lib/calendar-api';
import { isCalendarDate } from '@/lib/calendar-validation';
import { cooperationApi, cooperationKeys } from '@/lib/cooperation-api';
import type { CooperationTherapist } from '@/lib/cooperation-api';
import {
  patientAppointmentsApi,
  patientAppointmentError,
  patientAppointmentErrorCode,
  patientAppointmentKeys,
} from '@/lib/patient-appointments-api';
import type {
  PatientBookingPayload,
  PatientBookingSettings,
} from '@/lib/patient-appointments-api';

type BookingReview = Pick<
  PatientBookingPayload,
  'therapistId' | 'startsAt' | 'durationMinutes'
>;

export default function PatientBookingDialog({
  accountId,
  onClose,
  onSaved,
  onFindTherapist,
}: {
  accountId: string;
  onClose: () => void;
  onSaved: () => void;
  onFindTherapist: () => void;
}) {
  const [therapistId, setTherapistId] = useState('');
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);

  const { confirmLeave, discardDialog } = useUnsavedChanges(
    dirty,
    busy,
  );

  const cooperations = useQuery({
    queryKey: cooperationKeys.therapists(accountId),
    queryFn: ({ signal }) => cooperationApi.therapists(signal),
    staleTime: 0,
    refetchOnWindowFocus: !busy,
  });

  const selected = cooperations.data?.find(
    (item) => item.therapist.id === therapistId,
  )?.therapist;

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
            className="appointment-wide-dialog patient-booking-dialog fixed left-1/2 top-1/2 z-50 -translate-x-1/2 -translate-y-1/2 rounded-2xl border bg-background shadow-xl"
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
              Zarezerwuj wizytę
            </Dialog.Title>

            <Dialog.Description className="sr-only">
              Wybierz terapeutę i termin, a następnie potwierdź
              rezerwację. Możesz dodać opcjonalną wiadomość.
            </Dialog.Description>

            <div className="patient-booking-content mt-6">
              {cooperations.isPending ? (
                <p role="status">Wczytywanie współprac…</p>
              ) : cooperations.isError ? (
                <div className="space-y-3">
                  <p role="alert" className="text-sm text-destructive">
                    {patientAppointmentError(cooperations.error)}
                  </p>

                  <Button
                    type="button"
                    variant="outline"
                    disabled={cooperations.isFetching}
                    onClick={() => void cooperations.refetch()}
                  >
                    Spróbuj ponownie
                  </Button>
                </div>
              ) : cooperations.data.length === 0 ? (
                <section className="space-y-4 rounded-xl border border-dashed p-5">
                  <h2 className="text-2xl">
                    Najpierw rozpocznij współpracę
                  </h2>

                  <p className="text-sm leading-relaxed text-muted-foreground">
                    Wizytę zarezerwujesz u terapeuty, który
                    zaakceptował współpracę i powiązał konto
                    z Twoją kartą.
                  </p>

                  <Button
                    type="button"
                    onClick={() =>
                      confirmLeave(() => {
                        onClose();
                        onFindTherapist();
                      })
                    }
                  >
                    Znajdź terapeutę
                  </Button>
                </section>
              ) : (
                <>
                  <div className="space-y-2 rounded-2xl border bg-card p-4">
                    <Label htmlFor="booking-therapist">
                      Terapeuta
                    </Label>

                    <select
                      id="booking-therapist"
                      value={therapistId}
                      disabled={busy}
                      onChange={(event) => {
                        const nextId = event.target.value;

                        if (nextId === therapistId) return;

                        confirmLeave(() => {
                          setTherapistId(nextId);
                          setDirty(Boolean(nextId));
                        });
                      }}
                      className="h-11 w-full rounded-md border bg-background px-3 text-sm"
                    >
                      <option value="">Wybierz terapeutę</option>

                      {cooperations.data.map((item) => (
                        <option
                          key={item.requestId}
                          value={item.therapist.id}
                        >
                          {item.therapist.firstName}{' '}
                          {item.therapist.lastName}
                          {item.therapist.practiceName
                            ? ` — ${item.therapist.practiceName}`
                            : ''}
                        </option>
                      ))}
                    </select>
                  </div>

                  {selected && (
                    <BookingSettings
                      key={selected.id}
                      accountId={accountId}
                      therapist={selected}
                      onDirty={() => setDirty(true)}
                      onBusyChange={setBusy}
                      onSaved={() => {
                        setDirty(false);
                        onSaved();
                      }}
                    />
                  )}
                </>
              )}
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

type BookingProps = {
  accountId: string;
  therapist: CooperationTherapist;
  onDirty: () => void;
  onBusyChange: (busy: boolean) => void;
  onSaved: () => void;
};

function BookingSettings(props: BookingProps) {
  const query = useQuery({
    queryKey: [
      ...patientAppointmentKeys.all(props.accountId),
      'booking-settings',
      props.therapist.id,
    ],
    queryFn: ({ signal }) =>
      patientAppointmentsApi.settings(props.therapist.id, signal),
    staleTime: 0,
    refetchOnWindowFocus: false,
  });

  if (query.isPending) {
    return <p role="status">Wczytywanie zasad rezerwacji…</p>;
  }

  if (query.isError) {
    return (
      <div className="space-y-3">
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
    );
  }

  if (query.data.allowedDurations.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Terapeuta nie udostępnił wariantów długości wizyt.
      </p>
    );
  }

  return <BookingFields {...props} settings={query.data} />;
}

function BookingFields({
  accountId,
  therapist,
  settings,
  onDirty,
  onBusyChange,
  onSaved,
}: BookingProps & { settings: PatientBookingSettings }) {
  const now = useCurrentTime();
  const queryClient = useQueryClient();
  const messageId = useId();
  const saving = useRef(false);

  const [date, setDate] = useState(() =>
    warsawDateKey(new Date()),
  );
  const [duration, setDuration] = useState(
    settings.allowedDurations[0],
  );
  const [startsAt, setStartsAt] = useState('');
  const [patientMessage, setPatientMessage] = useState('');
  const [review, setReview] = useState<BookingReview | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const slots = useQuery({
    queryKey: [
      ...patientAppointmentKeys.all(accountId),
      'slots',
      therapist.id,
      date,
      duration,
    ],
    queryFn: ({ signal }) =>
      patientAppointmentsApi.slots(
        therapist.id,
        date,
        duration,
        signal,
      ),
    enabled:
      isCalendarDate(date) &&
      settings.allowedDurations.includes(duration),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const mutation = useMutation({
    mutationFn: patientAppointmentsApi.book,
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => onSaved(),
    onError: (error) => {
      const code = patientAppointmentErrorCode(error);
      const nextErrors = getCalendarFieldErrors(error);

      if (
        code === 'slot_conflict' ||
        code === 'slot_unavailable' ||
        code === 'appointment_in_past'
      ) {
        setReview(null);
        setStartsAt('');

        nextErrors.startsAt =
          'Ten termin nie jest już dostępny. Wybierz inną godzinę.';
      } else if (
        nextErrors.date ||
        nextErrors.durationMinutes ||
        nextErrors.startsAt ||
        nextErrors.therapistId
      ) {
        setReview(null);
      }

      setErrors(nextErrors);
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
          queryClient.invalidateQueries({
            queryKey: cooperationKeys.all(accountId),
          }),
        ]);
      } finally {
        saving.current = false;
        onBusyChange(false);
      }
    },
  });

  function clearErrors() {
    setErrors({});
    mutation.reset();
    onDirty();
  }

  function chooseTerm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (mutation.isPending || saving.current) return;

    const form = new FormData(event.currentTarget);
    const selectedStart = String(form.get('startsAt') ?? '');

    const selectedSlot = slots.data?.slots.find(
      (slot) =>
        new Date(slot.startsAt).getTime() ===
        new Date(selectedStart).getTime(),
    );

    const next: Record<string, string> = {};

    if (!isCalendarDate(date)) {
      next.date = 'Wybierz poprawną datę.';
    }

    if (!settings.allowedDurations.includes(duration)) {
      next.durationMinutes = 'Wybierz dozwoloną długość.';
    }

    if (
      !selectedSlot ||
      slots.isPending ||
      slots.isFetching ||
      slots.isError ||
      new Date(selectedSlot.startsAt).getTime() <= now
    ) {
      next.startsAt = 'Wybierz aktualny wolny termin.';
    }

    setErrors(next);

    if (Object.keys(next).length === 0 && selectedSlot) {
      mutation.reset();

      setReview({
        therapistId: therapist.id,
        startsAt: selectedSlot.startsAt,
        durationMinutes: duration,
      });
    }
  }

  function confirmBooking(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!review || saving.current || mutation.isPending) return;

    const next: Record<string, string> = {};

    if (patientMessage.length > 500) {
      next.patientMessage =
        'Wiadomość może mieć maksymalnie 500 znaków.';
    }

    if (new Date(review.startsAt).getTime() <= now) {
      next.startsAt = 'Ten termin już minął. Wybierz inną godzinę.';
    }

    setErrors(next);

    if (next.startsAt) {
      setReview(null);
      setStartsAt('');
      return;
    }

    if (Object.keys(next).length > 0) return;

    saving.current = true;

    mutation.mutate({
      ...review,
      patientMessage: patientMessage.trim() || null,
    });
  }

  if (review) {
    const expired =
      new Date(review.startsAt).getTime() <= now;

    return (
      <form
        noValidate
        onSubmit={confirmBooking}
        className="space-y-5 rounded-2xl border bg-primary/5 p-4 sm:p-5"
      >
        <header>
          <p className="text-xs font-semibold uppercase tracking-widest text-primary">
            Podsumowanie
          </p>
          <h2 className="mt-2 text-3xl">Potwierdź wizytę</h2>
        </header>

        <fieldset
          disabled={mutation.isPending}
          className="grid min-w-0 gap-6 lg:grid-cols-2"
        >
          <div className="space-y-5">
            <dl className="grid gap-4 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-muted-foreground">Terapeuta</dt>
                <dd className="mt-1 font-medium">
                  {therapist.firstName} {therapist.lastName}
                </dd>
              </div>

              <div>
                <dt className="text-muted-foreground">Gabinet</dt>
                <dd className="mt-1 break-words">
                  {[therapist.practiceName, therapist.city]
                    .filter(Boolean)
                    .join(' · ') || 'Nie podano danych gabinetu'}
                </dd>
              </div>

              <div>
                <dt className="text-muted-foreground">Termin</dt>
                <dd className="mt-1 font-medium">
                  {appointmentDateTime(review.startsAt)}
                </dd>
              </div>

              <div>
                <dt className="text-muted-foreground">Długość</dt>
                <dd className="mt-1">
                  {review.durationMinutes} minut
                </dd>
              </div>
            </dl>

            <p className="rounded-xl border bg-background/60 p-4 text-sm leading-relaxed text-muted-foreground">
              Wizytę możesz samodzielnie odwołać najpóźniej
              24 godziny przed rozpoczęciem. Później skontaktuj
              się z gabinetem.
            </p>
          </div>

          <div className="min-w-0 space-y-3 rounded-xl border bg-background p-4">
            <Label htmlFor={messageId}>
              Z czym chcesz się zgłosić? — opcjonalnie
            </Label>

            <p
              id={`${messageId}-help`}
              className="text-xs leading-relaxed text-muted-foreground"
            >
              Krótko opisz dolegliwość lub cel wizyty.
              Nie wpisuj PESEL-u ani szczegółowej historii medycznej.
              Wiadomość zobaczy terapeuta prowadzący wizytę.
            </p>

            <Textarea
              id={messageId}
              name="patientMessage"
              rows={5}
              maxLength={500}
              value={patientMessage}
              placeholder="Np. Pierwsza konsultacja dotycząca bólu barku od dwóch tygodni."
              aria-invalid={Boolean(errors.patientMessage)}
              aria-describedby={`${messageId}-help ${messageId}-count ${
                errors.patientMessage ? `${messageId}-error` : ''
              }`}
              onChange={(event) => {
                setPatientMessage(event.target.value);
                clearErrors();
              }}
            />

            <p
              id={`${messageId}-count`}
              className="text-right text-xs text-muted-foreground"
            >
              {patientMessage.length}/500 znaków
            </p>

            {errors.patientMessage && (
              <p
                id={`${messageId}-error`}
                role="alert"
                className="text-sm text-destructive"
              >
                {errors.patientMessage}
              </p>
            )}
          </div>
        </fieldset>

        {expired && (
          <p role="alert" className="text-sm text-destructive">
            Ten termin już minął. Wróć do wyboru godziny.
          </p>
        )}

        {mutation.isError && (
          <p
            role="alert"
            className="rounded-xl border border-destructive/30 bg-background p-4 text-sm text-destructive"
          >
            {patientAppointmentError(mutation.error)}
          </p>
        )}

        <div className="sticky bottom-0 space-y-3 border-t bg-background/95 pb-2 pt-4 backdrop-blur">
          <Button
            type="submit"
            className="w-full"
            disabled={mutation.isPending || expired}
          >
            {mutation.isPending
              ? 'Rezerwowanie…'
              : 'Potwierdź rezerwację'}
          </Button>

          <Button
            type="button"
            variant="outline"
            className="w-full"
            disabled={mutation.isPending}
            onClick={() => setReview(null)}
          >
            Wróć do wyboru
          </Button>
        </div>
      </form>
    );
  }

  return (
    <form noValidate onSubmit={chooseTerm} className="space-y-5">
      <fieldset
        disabled={mutation.isPending}
        className="min-w-0 space-y-5"
      >
        <AppointmentSlotSelector
          date={date}
          onDateChange={(next) => {
            setDate(next);
            setStartsAt('');
            clearErrors();
          }}
          duration={duration}
          durations={settings.allowedDurations}
          onDurationChange={(next) => {
            setDuration(next);
            setStartsAt('');
            clearErrors();
          }}
          slots={slots.data?.slots ?? []}
          value={startsAt}
          onChange={(next) => {
            setStartsAt(next);
            clearErrors();
          }}
          now={now}
          loading={slots.isPending || slots.isFetching}
          error={
            slots.isError
              ? patientAppointmentError(slots.error)
              : undefined
          }
          fieldErrors={errors}
          onRefresh={() => void slots.refetch()}
          disabled={mutation.isPending}
        />

        {mutation.isError && (
          <p
            role="alert"
            className="rounded-xl border p-4 text-sm text-destructive"
          >
            {patientAppointmentError(mutation.error)}
          </p>
        )}

        <div className="sticky bottom-0 border-t bg-background/95 pb-2 pt-4 backdrop-blur">
          <Button
            type="submit"
            className="w-full"
            disabled={
              slots.isPending ||
              slots.isFetching ||
              slots.isError ||
              mutation.isPending
            }
          >
            Przejdź do podsumowania
          </Button>
        </div>
      </fieldset>
    </form>
  );
}