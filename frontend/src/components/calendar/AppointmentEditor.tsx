'use client';

import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Input, Label, Textarea } from '@/components/ui/primitives';
import {
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
import { validateAppointment } from '@/lib/calendar-validation';
import type {
  AppointmentDraft,
  CalendarFormErrors,
} from '@/lib/calendar-validation';
import type { Appointment } from '@/types/api';

function ErrorText({ id, message }: { id: string; message?: string }) {
  return message ? (
    <p id={id} className="text-sm text-destructive">{message}</p>
  ) : null;
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
  const [base] = useState<AppointmentDraft>(() => {
    const duration =
      appointment &&
      settings.allowedDurations.includes(appointment.durationMinutes)
        ? appointment.durationMinutes
        : settings.allowedDurations[0] ?? 30;

    return {
      patientId: appointment?.patientId ?? '',
      date: initialDate,
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
  const [patientName, setPatientName] = useState(appointment?.patientName ?? '');
  const [errors, setErrors] = useState<CalendarFormErrors>({});

  const mutation = useMutation({
    mutationFn: (payload: AppointmentPayload) =>
      appointment
        ? appointmentsApi.update(appointment.id, payload)
        : appointmentsApi.create(payload),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => {
      onDirtyChange(false);
      onSaved(appointment ? 'Wizyta została zmieniona.' : 'Wizyta została utworzona.');
    },
    onError: (error) => setErrors(getCalendarFieldErrors(error)),
    onSettled: async () => {
      onBusyChange(false);
      await onRefresh();
    },
  });

  function change(patch: Partial<AppointmentDraft>) {
    const next = { ...draft, ...patch };
    setDraft(next);
    setErrors({});
    mutation.reset();
    onDirtyChange(JSON.stringify(next) !== JSON.stringify(base));
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mutation.isPending) return;

    const form = new FormData(event.currentTarget);
    const next = {
      ...draft,
      startsAt: String(form.get('startsAt') ?? ''),
    };

    const nextErrors = validateAppointment(
      next,
      settings.allowedDurations,
      Date.now(),
    );

    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;

    mutation.mutate({
      patientId: next.patientId,
      startsAt: next.startsAt,
      durationMinutes: next.durationMinutes,
      reason: next.reason.trim() || null,
      notes: next.notes.trim() || null,
    });
  }

  const expired =
    appointment && new Date(appointment.startsAt).getTime() <= now;

  return (
    <form noValidate onSubmit={submit} className="space-y-5">
      <fieldset disabled={mutation.isPending} className="min-w-0 space-y-5">
        <PatientPicker
          accountId={accountId}
          selectedId={draft.patientId}
          selectedName={patientName}
          error={errors.patientId}
          onSelect={(id, name) => {
            setPatientName(name);
            change({ patientId: id });
          }}
        />

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-2">
            <Label htmlFor="appointment-date">Data</Label>
            <Input
              id="appointment-date"
              type="date"
              value={draft.date}
              min={warsawDateKey(new Date(now))}
              aria-invalid={Boolean(errors.date)}
              aria-describedby="appointment-date-error"
              onChange={(event) =>
                change({ date: event.target.value, startsAt: '' })
              }
            />
            <ErrorText id="appointment-date-error" message={errors.date} />
          </div>

          <div className="space-y-2">
            <Label htmlFor="appointment-duration">Długość wizyty</Label>
            <select
              id="appointment-duration"
              value={draft.durationMinutes}
              aria-invalid={Boolean(errors.durationMinutes)}
              aria-describedby="appointment-duration-error"
              onChange={(event) =>
                change({
                  durationMinutes: Number(event.target.value),
                  startsAt: '',
                })
              }
              className="h-10 w-full rounded-md border bg-background px-3 text-sm"
            >
              {settings.allowedDurations.map((duration) => (
                <option key={duration} value={duration}>{duration} minut</option>
              ))}
            </select>
            <ErrorText
              id="appointment-duration-error"
              message={errors.durationMinutes}
            />
          </div>
        </div>

        {draft.date && (
          <SlotPicker
            accountId={accountId}
            date={draft.date}
            duration={draft.durationMinutes}
            appointment={appointment}
            now={now}
            value={draft.startsAt}
            error={errors.startsAt}
            onChange={(startsAt) => change({ startsAt })}
          />
        )}

        <div className="space-y-2">
          <Label htmlFor="appointment-reason">Powód wizyty</Label>
          <Textarea
            id="appointment-reason"
            rows={2}
            maxLength={500}
            value={draft.reason}
            aria-invalid={Boolean(errors.reason)}
            aria-describedby="appointment-reason-error"
            onChange={(event) => change({ reason: event.target.value })}
          />
          <ErrorText id="appointment-reason-error" message={errors.reason} />
          <p className="text-xs text-muted-foreground">{draft.reason.length}/500</p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="appointment-notes">Notatka terapeuty</Label>
          <Textarea
            id="appointment-notes"
            rows={4}
            maxLength={2000}
            value={draft.notes}
            aria-invalid={Boolean(errors.notes)}
            aria-describedby="appointment-notes-error"
            onChange={(event) => change({ notes: event.target.value })}
          />
          <ErrorText id="appointment-notes-error" message={errors.notes} />
          <p className="text-xs text-muted-foreground">{draft.notes.length}/2000</p>
        </div>

        {expired && (
          <p role="alert" className="text-sm text-destructive">
            Wizyta już się rozpoczęła. Nie można zapisać zmiany terminu.
          </p>
        )}

        {Object.keys(errors).length > 0 && (
          <div role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">
            <p className="font-medium">Popraw formularz:</p>
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {[...new Set(Object.values(errors))].map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          </div>
        )}

        {mutation.isError && Object.keys(errors).length === 0 && (
          <p role="alert" className="text-sm text-destructive">
            {getCalendarError(mutation.error)}
          </p>
        )}

        <p className="text-xs leading-relaxed text-muted-foreground">
          Godziny dotyczą Europe/Warsaw. Bufor po wizycie:
          {' '}{settings.bufferMinutes} min. Dostępność zostanie sprawdzona
          ponownie przy zapisie.
        </p>

        <Button
          type="submit"
          disabled={
            mutation.isPending ||
            Boolean(expired) ||
            settings.allowedDurations.length === 0
          }
        >
          {mutation.isPending
            ? 'Zapisywanie…'
            : appointment ? 'Zapisz zmiany' : 'Utwórz wizytę'}
        </Button>
      </fieldset>
    </form>
  );
}

function SlotPicker({
  accountId,
  date,
  duration,
  appointment,
  now,
  value,
  error,
  onChange,
}: {
  accountId: string;
  date: string;
  duration: number;
  appointment?: Appointment;
  now: number;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const query = useQuery({
    queryKey: ['therapist-available-slots', accountId, date, duration],
    queryFn: ({ signal }) => appointmentsApi.slots(date, duration, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const options: AvailableSlot[] = (query.data?.slots ?? []).filter(
    (slot) => new Date(slot.startsAt).getTime() > now,
  );

  if (
    appointment &&
    warsawDateKey(appointment.startsAt) === date &&
    appointment.durationMinutes === duration &&
    new Date(appointment.startsAt).getTime() > now &&
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

  options.sort(
    (a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime(),
  );

  const selected =
    options.find(
      (slot) =>
        new Date(slot.startsAt).getTime() === new Date(value).getTime(),
    )?.startsAt ?? '';

  return (
    <section className="space-y-3 rounded-xl border p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Label htmlFor="appointment-slot">Godzina wizyty</Label>
        <Button
          type="button"
          variant="outline"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          {query.isFetching ? 'Odświeżanie…' : 'Odśwież terminy'}
        </Button>
      </div>

      {query.isPending ? (
        <p role="status" className="text-sm">Wczytywanie terminów…</p>
      ) : query.isError ? (
        <p role="alert" className="text-sm text-destructive">
          {getCalendarError(query.error)}
        </p>
      ) : (
        <>
          <select
            id="appointment-slot"
            name="startsAt"
            value={selected}
            disabled={query.isFetching || options.length === 0}
            aria-invalid={Boolean(error)}
            aria-describedby="appointment-slot-error"
            onChange={(event) => onChange(event.target.value)}
            className="h-10 w-full rounded-md border bg-background px-3 text-sm"
          >
            <option value="">Wybierz godzinę</option>
            {options.map((slot) => (
              <option key={slot.startsAt} value={slot.startsAt}>
                {appointmentTime(slot.startsAt)} – {appointmentTime(slot.endsAt)}
                {appointment &&
                new Date(slot.startsAt).getTime() ===
                  new Date(appointment.startsAt).getTime()
                  ? ' (obecny termin)'
                  : ''}
              </option>
            ))}
          </select>

          {options.length === 0 && (
            <p className="text-sm text-muted-foreground">
              Brak wolnych terminów. Wybierz inną datę lub długość wizyty.
            </p>
          )}
        </>
      )}

      <ErrorText id="appointment-slot-error" message={error} />

      {appointment && (
        <p className="text-xs leading-relaxed text-muted-foreground">
          Obecny termin można zachować. API nie pomija jeszcze edytowanej
          wizyty przy wyliczaniu pozostałych wolnych godzin.
        </p>
      )}
    </section>
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
    queryKey: ['appointment-patients', accountId, search, page],
    queryFn: ({ signal }) => appointmentsApi.patients(search, page, signal),
    staleTime: 0,
  });

  function applySearch() {
    setSearch(draft.trim());
    setPage(1);
    if (search === draft.trim() && page === 1) void query.refetch();
  }

  return (
    <section
      className="space-y-3 rounded-xl border p-4"
      aria-labelledby="appointment-patient-title"
    >
      <h2 id="appointment-patient-title" className="text-2xl">Pacjent</h2>

      {selectedId && (
        <p role="status" className="rounded-lg bg-primary/10 p-3 text-sm">
          Wybrano: <strong>{selectedName}</strong>
        </p>
      )}

      <Label htmlFor="appointment-patient-search">
        Imię, nazwisko, e-mail lub telefon
      </Label>

      <div className="flex flex-col gap-2 sm:flex-row">
        <Input
          id="appointment-patient-search"
          value={draft}
          maxLength={100}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              applySearch();
            }
          }}
        />
        <Button type="button" variant="outline" onClick={applySearch}>
          Szukaj
        </Button>
      </div>

      <ErrorText id="appointment-patient-error" message={error} />

      {query.isPending ? (
        <p role="status" className="text-sm">Wczytywanie pacjentów…</p>
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
            <p className="text-sm text-muted-foreground">Nie znaleziono pacjentów.</p>
          )}

          <div className="max-h-44 space-y-2 overflow-y-auto">
            {query.data.items.map((patient) => (
              <label
                key={patient.id}
                className="flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm"
              >
                <input
                  type="radio"
                  name="patient-choice"
                  checked={selectedId === patient.id}
                  aria-describedby={error ? 'appointment-patient-error' : undefined}
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
                    {patient.email || 'Brak e-maila'} · {patient.phoneNumber || 'Brak telefonu'}
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
                disabled={!query.data.hasPrevious || query.isFetching}
                onClick={() => setPage((current) => current - 1)}
              >
                Poprzednia
              </Button>
              <span className="text-xs">
                {query.data.pageNumber}/{query.data.totalPages}
              </span>
              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasNext || query.isFetching}
                onClick={() => setPage((current) => current + 1)}
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