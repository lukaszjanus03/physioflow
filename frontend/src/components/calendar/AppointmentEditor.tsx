'use client';

import { useMutation, useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { useId, useRef, useState } from 'react';
import type { FormEvent } from 'react';

import AppointmentSlotSelector from '@/components/calendar/AppointmentSlotSelector';
import { Button } from '@/components/ui/button';
import { Input, Label, Textarea } from '@/components/ui/primitives';
import {
  appointmentDateTime,
  appointmentTime,
  warsawDateKey,
} from '@/lib/appointment-display';
import { appointmentsApi } from '@/lib/appointments-api';
import type {
  AppointmentPayload,
  AvailableSlot,
} from '@/lib/appointments-api';
import {
  getCalendarError,
  getCalendarFieldErrors,
} from '@/lib/calendar-api';
import type { CalendarSettings } from '@/lib/calendar-api';
import {
  isCalendarDate,
  validateAppointment,
} from '@/lib/calendar-validation';
import type {
  AppointmentDraft,
  CalendarFormErrors,
} from '@/lib/calendar-validation';
import type { Appointment } from '@/types/api';

type Step = 0 | 1 | 2;

const steps = [
  { label: 'Pacjent', title: 'Wybierz pacjenta' },
  { label: 'Termin', title: 'Wybierz termin wizyty' },
  { label: 'Potwierdzenie', title: 'Szczegóły i potwierdzenie' },
] as const;

function ErrorText({
  id,
  message,
}: {
  id: string;
  message?: string;
}) {
  return message ? (
    <p id={id} className="text-sm text-destructive">
      {message}
    </p>
  ) : null;
}

function stepForErrors(errors: CalendarFormErrors): Step {
  if (errors.patientId) return 0;

  if (errors.date || errors.durationMinutes || errors.startsAt) {
    return 1;
  }

  return 2;
}

export default function AppointmentEditor({
  accountId,
  settings,
  appointment,
  initialDate,
  now,
  onDirtyChange,
  onBusyChange,
  onRefresh,
  onSaved,
}: {
  accountId: string;
  settings: CalendarSettings;
  appointment?: Appointment;
  initialDate: string;
  now: number;
  onDirtyChange: (dirty: boolean) => void;
  onBusyChange: (busy: boolean) => void;
  onRefresh: () => Promise<void>;
  onSaved: (message: string) => void;
}) {
  const id = useId();
  const stepHeading = useRef<HTMLHeadingElement>(null);
  const saving = useRef(false);

  const [base] = useState<AppointmentDraft>(() => {
    const duration =
      appointment &&
      settings.allowedDurations.includes(appointment.durationMinutes)
        ? appointment.durationMinutes
        : settings.allowedDurations[0] ?? 30;

    const today = warsawDateKey(new Date(now));

    return {
      patientId: appointment?.patientId ?? '',
      date:
        isCalendarDate(initialDate) && initialDate >= today
          ? initialDate
          : today,
      durationMinutes: duration,
      startsAt:
        appointment && duration === appointment.durationMinutes
          ? appointment.startsAt
          : '',
      reason: appointment?.reason ?? '',
      notes: appointment?.notes ?? '',
    };
  });

  const [draft, setDraft] = useState(base);
  const [patientName, setPatientName] = useState(
    appointment?.patientName ?? '',
  );
  const [step, setStep] = useState<Step>(0);
  const [errors, setErrors] = useState<CalendarFormErrors>({});

  const slots = useQuery({
    queryKey: [
      'therapist-available-slots',
      accountId,
      draft.date,
      draft.durationMinutes,
    ],
    queryFn: ({ signal }) =>
      appointmentsApi.slots(
        draft.date,
        draft.durationMinutes,
        signal,
      ),
    enabled:
      step >= 1 &&
      isCalendarDate(draft.date) &&
      settings.allowedDurations.includes(draft.durationMinutes),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const options: AvailableSlot[] = [...(slots.data?.slots ?? [])];

  const keepOriginal =
    appointment &&
    warsawDateKey(appointment.startsAt) === draft.date &&
    appointment.durationMinutes === draft.durationMinutes &&
    new Date(appointment.startsAt).getTime() > now;

  if (
    keepOriginal &&
    !options.some(
      (slot) =>
        new Date(slot.startsAt).getTime() ===
        new Date(appointment.startsAt).getTime(),
    )
  ) {
    options.push({
      startsAt: appointment.startsAt,
      endsAt: appointment.endsAt,
    });
  }

  const selectedSlot = options.find(
    (slot) =>
      new Date(slot.startsAt).getTime() ===
        new Date(draft.startsAt).getTime() &&
      new Date(slot.startsAt).getTime() > now &&
      warsawDateKey(slot.startsAt) === draft.date,
  );

  const checkingSlots = slots.isPending || slots.isFetching;

  const slotReady = Boolean(
    selectedSlot &&
      !checkingSlots &&
      !slots.isError &&
      settings.allowedDurations.includes(draft.durationMinutes),
  );

  const expired = Boolean(
    appointment &&
      new Date(appointment.startsAt).getTime() <= now,
  );

  function goToStep(next: Step) {
    setStep(next);

    window.requestAnimationFrame(() => {
      stepHeading.current?.focus();
    });
  }

  const mutation = useMutation({
    mutationFn: (payload: AppointmentPayload) =>
      appointment
        ? appointmentsApi.update(appointment.id, payload)
        : appointmentsApi.create(payload),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => {
      onDirtyChange(false);
      onSaved(
        appointment
          ? 'Wizyta została zmieniona.'
          : 'Wizyta została utworzona.',
      );
    },
    onError: (error) => {
      const nextErrors = getCalendarFieldErrors(error);

      const code = isAxiosError<{ code?: string }>(error)
        ? error.response?.data?.code
        : undefined;

      if (
        code === 'slot_conflict' ||
        code === 'slot_unavailable' ||
        code === 'appointment_in_past'
      ) {
        nextErrors.startsAt =
          'Ten termin nie jest już dostępny. Wybierz inną godzinę.';

        setDraft((current) => ({
          ...current,
          startsAt: '',
        }));
      }

      setErrors(nextErrors);

      if (Object.keys(nextErrors).length > 0) {
        goToStep(stepForErrors(nextErrors));
      }
    },
    onSettled: async () => {
      try {
        await onRefresh();
      } finally {
        saving.current = false;
        onBusyChange(false);
      }
    },
  });

  function change(patch: Partial<AppointmentDraft>) {
    const next = { ...draft, ...patch };

    setDraft(next);
    setErrors({});
    mutation.reset();

    onDirtyChange(JSON.stringify(next) !== JSON.stringify(base));
  }

  function validateCurrentDraft(includeNotes: boolean) {
    const nextErrors = validateAppointment(
      draft,
      settings.allowedDurations,
      now,
    );

    if (!includeNotes) {
      delete nextErrors.reason;
      delete nextErrors.notes;
    }

    if (!slotReady) {
      nextErrors.startsAt = checkingSlots
        ? 'Poczekaj na sprawdzenie dostępnych godzin.'
        : slots.isError
          ? 'Nie udało się sprawdzić godzin. Ponów pobieranie.'
          : 'Wybierz aktualny wolny termin.';
    }

    return nextErrors;
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (saving.current || mutation.isPending || expired) return;

    if (step === 0) {
      if (!draft.patientId) {
        setErrors({ patientId: 'Wybierz pacjenta.' });
        return;
      }

      setErrors({});
      goToStep(1);
      return;
    }

    if (step === 1) {
      const nextErrors = validateCurrentDraft(false);

      setErrors(nextErrors);

      if (Object.keys(nextErrors).length > 0) {
        goToStep(stepForErrors(nextErrors));
        return;
      }

      goToStep(2);
      return;
    }

    const submitter = (event.nativeEvent as SubmitEvent).submitter;

    // Ostateczny zapis wymaga użycia przycisku potwierdzenia.
    if (!submitter?.hasAttribute('data-save-appointment')) return;

    const nextErrors = validateCurrentDraft(true);

    setErrors(nextErrors);

    if (Object.keys(nextErrors).length > 0) {
      goToStep(stepForErrors(nextErrors));
      return;
    }

    if (!selectedSlot) return;

    saving.current = true;

    mutation.mutate({
      patientId: draft.patientId,
      startsAt: selectedSlot.startsAt,
      durationMinutes: draft.durationMinutes,
      reason: draft.reason.trim() || null,
      notes: draft.notes.trim() || null,
    });
  }

  return (
    <form noValidate onSubmit={submit}>
      <fieldset
        disabled={mutation.isPending}
        className="appointment-editor-fields appointment-wizard-fields min-w-0"
      >
        <nav aria-label="Etapy tworzenia wizyty">
          <ol className="grid grid-cols-3 gap-2 sm:gap-4">
            {steps.map((item, index) => {
              const active = step === index;
              const completed = step > index;

              return (
                <li
                  key={item.label}
                  aria-current={active ? 'step' : undefined}
                  className={`flex items-center gap-2 rounded-xl border px-3 py-3 sm:gap-3 sm:px-4 ${
                    active
                      ? 'border-primary bg-primary/5'
                      : completed
                        ? 'border-primary/20 bg-primary/5'
                        : 'bg-muted/30 text-muted-foreground'
                  }`}
                >
                  <span
                    aria-hidden="true"
                    className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${
                      active || completed
                        ? 'bg-primary text-primary-foreground'
                        : 'border bg-background'
                    }`}
                  >
                    {index + 1}
                  </span>

                  <span className="break-words text-xs font-medium sm:text-sm">
                    {item.label}
                  </span>
                </li>
              );
            })}
          </ol>
        </nav>

        <div className="mb-4 mt-5">
          <h2
            ref={stepHeading}
            tabIndex={-1}
            className="text-3xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            {steps[step].title}
          </h2>

          <p className="mt-1 text-sm text-muted-foreground">
            {step === 0
              ? 'Wyszukaj pacjenta i wybierz jego kartę.'
              : step === 1
                ? `Pacjent: ${patientName}. Wybierz długość, dzień i godzinę wizyty.`
                : 'Sprawdź dane wizyty. Powód i notatka są opcjonalne.'}
          </p>
        </div>

        <div hidden={step !== 0}>
          <div className="mx-auto max-w-3xl">
            <PatientPicker
              accountId={accountId}
              selectedId={draft.patientId}
              selectedName={patientName}
              error={errors.patientId}
              onSelect={(patientId, name) => {
                setPatientName(name);
                change({ patientId });
              }}
            />
          </div>
        </div>

        <div hidden={step !== 1}>
          <AppointmentSlotSelector
            date={draft.date}
            onDateChange={(date) =>
              change({ date, startsAt: '' })
            }
            duration={draft.durationMinutes}
            durations={settings.allowedDurations}
            onDurationChange={(durationMinutes) =>
              change({ durationMinutes, startsAt: '' })
            }
            slots={options}
            value={draft.startsAt}
            onChange={(startsAt) => change({ startsAt })}
            now={now}
            loading={checkingSlots}
            error={
              slots.isError
                ? getCalendarError(slots.error)
                : undefined
            }
            fieldErrors={errors}
            onRefresh={() => void slots.refetch()}
            disabled={mutation.isPending}
            currentStartsAt={appointment?.startsAt}
          />

          {appointment && (
            <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
              Możesz zachować obecny termin. Przy wyliczaniu
              pozostałych godzin API nadal traktuje edytowaną
              wizytę jako zajęty czas.
            </p>
          )}
        </div>

        <div hidden={step !== 2}>
          <section className="space-y-5">
            <dl className="grid gap-4 rounded-2xl border border-primary/20 bg-primary/5 p-4 sm:grid-cols-3 sm:p-5">
              <div>
                <dt className="text-xs text-muted-foreground">
                  Pacjent
                </dt>
                <dd className="mt-2 break-words text-sm font-semibold">
                  {patientName || 'Nie wybrano pacjenta'}
                </dd>
              </div>

              <div>
                <dt className="text-xs text-muted-foreground">
                  Termin
                </dt>
                <dd className="mt-2 text-sm font-semibold">
                  {Number.isFinite(
                    new Date(draft.startsAt).getTime(),
                  )
                    ? appointmentDateTime(draft.startsAt)
                    : 'Nie wybrano terminu'}

                  {selectedSlot && (
                    <span className="mt-1 block text-xs font-normal text-muted-foreground">
                      Do {appointmentTime(selectedSlot.endsAt)}
                    </span>
                  )}
                </dd>
              </div>

              <div>
                <dt className="text-xs text-muted-foreground">
                  Długość i bufor
                </dt>
                <dd className="mt-2 text-sm font-semibold">
                  {draft.durationMinutes} minut
                  <span className="mt-1 block text-xs font-normal text-muted-foreground">
                    Bufor po wizycie: {settings.bufferMinutes} min
                  </span>
                </dd>
              </div>
            </dl>

            <div className="grid gap-5 rounded-2xl border bg-card p-4 md:grid-cols-2 sm:p-5">
              <div className="min-w-0 space-y-2">
                <Label htmlFor={`${id}-reason`}>
                  Powód wizyty — opcjonalnie
                </Label>

                <Textarea
                  id={`${id}-reason`}
                  rows={3}
                  maxLength={500}
                  value={draft.reason}
                  aria-invalid={Boolean(errors.reason)}
                  aria-describedby={`${id}-reason-help ${id}-reason-error`}
                  onChange={(event) =>
                    change({ reason: event.target.value })
                  }
                />

                <p
                  id={`${id}-reason-help`}
                  className="text-xs text-muted-foreground"
                >
                  {draft.reason.length}/500 znaków
                </p>

                <ErrorText
                  id={`${id}-reason-error`}
                  message={errors.reason}
                />
              </div>

              <div className="min-w-0 space-y-2">
                <Label htmlFor={`${id}-notes`}>
                  Notatka terapeuty — opcjonalnie
                </Label>

                <Textarea
                  id={`${id}-notes`}
                  rows={3}
                  maxLength={2000}
                  value={draft.notes}
                  aria-invalid={Boolean(errors.notes)}
                  aria-describedby={`${id}-notes-help ${id}-notes-error`}
                  onChange={(event) =>
                    change({ notes: event.target.value })
                  }
                />

                <p
                  id={`${id}-notes-help`}
                  className="text-xs text-muted-foreground"
                >
                  {draft.notes.length}/2000 znaków
                </p>

                <ErrorText
                  id={`${id}-notes-error`}
                  message={errors.notes}
                />
              </div>
            </div>

            {checkingSlots ? (
              <p
                role="status"
                className="text-sm text-muted-foreground"
              >
                Sprawdzanie aktualnej dostępności terminu…
              </p>
            ) : !slotReady ? (
              <p role="alert" className="text-sm text-destructive">
                Nie można potwierdzić dostępności wybranego
                terminu. Wróć do poprzedniego kroku i sprawdź
                godziny.
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                Dostępność zostanie ponownie sprawdzona przez
                serwer podczas zapisu.
              </p>
            )}
          </section>
        </div>

        {expired && (
          <p
            role="alert"
            className="mt-4 text-sm text-destructive"
          >
            Wizyta już się rozpoczęła. Nie można zapisać zmian.
          </p>
        )}

        {Object.keys(errors).length > 0 && (
          <div
            role="alert"
            className="mt-4 rounded-xl border border-destructive/30 p-4 text-sm text-destructive"
          >
            <p className="font-medium">Sprawdź formularz:</p>

            <ul className="mt-2 list-disc space-y-1 pl-5">
              {[...new Set(Object.values(errors))].map(
                (message) => (
                  <li key={message}>{message}</li>
                ),
              )}
            </ul>
          </div>
        )}

        {mutation.isError && (
          <p
            role="alert"
            className="mt-4 text-sm text-destructive"
          >
            {getCalendarError(mutation.error)}
          </p>
        )}

        <div className="mt-5 flex flex-col-reverse items-stretch justify-center gap-3 border-t pt-4 sm:flex-row">
          {step > 0 && (
            <Button
              type="button"
              variant="outline"
              className="min-h-11 sm:w-56"
              disabled={mutation.isPending}
              onClick={() =>
                goToStep(step === 2 ? 1 : 0)
              }
            >
              Wstecz
            </Button>
          )}

          {step < 2 ? (
            <Button
              type="submit"
              className="min-h-11 sm:w-56"
              disabled={
                mutation.isPending ||
                expired ||
                (step === 0 && !draft.patientId) ||
                (step === 1 && !slotReady)
              }
            >
              {step === 0
                ? 'Dalej: wybierz termin'
                : 'Dalej: szczegóły wizyty'}
            </Button>
          ) : (
            <Button
              type="submit"
              data-save-appointment="true"
              className="min-h-11 sm:w-64"
              disabled={
                mutation.isPending ||
                expired ||
                !slotReady
              }
            >
              {mutation.isPending
                ? 'Zapisywanie…'
                : appointment
                  ? 'Potwierdź zmiany'
                  : 'Potwierdź wizytę'}
            </Button>
          )}
        </div>
      </fieldset>
    </form>
  );
}

function PatientPicker({
  accountId,
  selectedId,
  selectedName,
  error,
  onSelect,
}: {
  accountId: string;
  selectedId: string;
  selectedName: string;
  error?: string;
  onSelect: (id: string, name: string) => void;
}) {
  const [draft, setDraft] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const query = useQuery({
    queryKey: [
      'appointment-patients',
      accountId,
      search,
      page,
    ],
    queryFn: ({ signal }) =>
      appointmentsApi.patients(search, page, signal),
    staleTime: 0,
  });

  function applySearch() {
    setSearch(draft.trim());
    setPage(1);

    if (search === draft.trim() && page === 1) {
      void query.refetch();
    }
  }

  return (
    <section className="space-y-4 rounded-2xl border bg-card p-4 sm:p-5">
      {selectedId && (
        <p
          role="status"
          className="rounded-xl bg-primary/10 p-3 text-sm text-primary"
        >
          Wybrano: <strong>{selectedName}</strong>
        </p>
      )}

      <div className="space-y-2">
        <Label htmlFor="appointment-patient-search">
          Imię, nazwisko, e-mail lub telefon
        </Label>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Input
            id="appointment-patient-search"
            value={draft}
            maxLength={100}
            onChange={(event) =>
              setDraft(event.target.value)
            }
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                applySearch();
              }
            }}
          />

          <Button
            type="button"
            variant="outline"
            onClick={applySearch}
          >
            Szukaj
          </Button>
        </div>
      </div>

      <ErrorText
        id="appointment-patient-error"
        message={error}
      />

      {query.isPending ? (
        <p role="status" className="text-sm">
          Wczytywanie pacjentów…
        </p>
      ) : query.isError ? (
        <div className="space-y-3">
          <p role="alert" className="text-sm text-destructive">
            {getCalendarError(query.error)}
          </p>

          <Button
            type="button"
            variant="outline"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            Ponów pobieranie
          </Button>
        </div>
      ) : (
        <>
          {query.data.items.length === 0 && (
            <p className="text-sm text-muted-foreground">
              Nie znaleziono pacjentów.
            </p>
          )}

          <div
            role="radiogroup"
            aria-label="Wybierz pacjenta"
            aria-describedby={
              error ? 'appointment-patient-error' : undefined
            }
            className="grid max-h-56 gap-3 overflow-y-auto p-1 sm:grid-cols-2"
          >
            {query.data.items.map((patient) => (
              <label
                key={patient.id}
                className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 text-sm transition-colors ${
                  selectedId === patient.id
                    ? 'border-primary bg-primary/5'
                    : 'hover:border-primary/40'
                }`}
              >
                <input
                  type="radio"
                  name="patient-choice"
                  checked={selectedId === patient.id}
                  onChange={() =>
                    onSelect(
                      patient.id,
                      `${patient.firstName} ${patient.lastName}`,
                    )
                  }
                  className="mt-1 accent-teal-600"
                />

                <span className="min-w-0">
                  <span className="block font-medium">
                    {patient.firstName} {patient.lastName}
                  </span>

                  <span className="mt-1 block break-words text-xs text-muted-foreground">
                    {patient.email || 'Brak e-maila'}
                  </span>

                  <span className="mt-1 block text-xs text-muted-foreground">
                    {patient.phoneNumber || 'Brak telefonu'}
                  </span>
                </span>
              </label>
            ))}
          </div>

          {query.data.totalPages > 1 && (
            <div className="flex items-center justify-between gap-2">
              <Button
                type="button"
                variant="outline"
                disabled={
                  !query.data.hasPrevious || query.isFetching
                }
                onClick={() =>
                  setPage((current) => current - 1)
                }
              >
                Poprzednia
              </Button>

              <span className="text-xs">
                {query.data.pageNumber}/{query.data.totalPages}
              </span>

              <Button
                type="button"
                variant="outline"
                disabled={
                  !query.data.hasNext || query.isFetching
                }
                onClick={() =>
                  setPage((current) => current + 1)
                }
              >
                Następna
              </Button>
            </div>
          )}
        </>
      )}
    </section>
  );
}