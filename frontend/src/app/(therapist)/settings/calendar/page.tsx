'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import type { FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import {
  CALENDAR_DAYS,
  calendarApi,
  calendarSettingsKey,
  getCalendarError,
  getCalendarFieldErrors,
} from '@/lib/calendar-api';
import type {
  CalendarException,
  CalendarInterval,
  CalendarSettings,
} from '@/lib/calendar-api';
import { validateCalendarSettings } from '@/lib/calendar-validation';
import type { CalendarFormErrors } from '@/lib/calendar-validation';
import { useAuthStore } from '@/stores/auth-store';

type Draft = Omit<CalendarSettings, 'bufferMinutes' | 'exceptions'> & {
  bufferMinutes: string;
  exceptions: (CalendarException & { localId: string })[];
};

function makeDraft(settings: CalendarSettings): Draft {
  return {
    bufferMinutes: String(settings.bufferMinutes),
    allowedDurations: [...settings.allowedDurations],
    weeklyHours: CALENDAR_DAYS.map((day) => ({
      dayOfWeek: day.value,
      intervals: (
        settings.weeklyHours.find((item) => item.dayOfWeek === day.value)
          ?.intervals ?? []
      ).map((interval) => ({
        start: interval.start.slice(0, 5),
        end: interval.end.slice(0, 5),
      })),
    })),
    exceptions: settings.exceptions.map((exception, index) => ({
      ...exception,
      localId: `saved-${index}`,
      intervals: (exception.intervals ?? []).map((interval) => ({
        start: interval.start.slice(0, 5),
        end: interval.end.slice(0, 5),
      })),
    })),
  };
}

function draftContent(draft: Draft) {
  return {
    bufferMinutes: draft.bufferMinutes,
    allowedDurations: [...draft.allowedDurations].sort((a, b) => a - b),
    weeklyHours: draft.weeklyHours,
    exceptions: draft.exceptions.map((exception) => ({
      date: exception.date,
      isDayOff: exception.isDayOff,
      intervals: exception.isDayOff ? [] : exception.intervals,
    })),
  };
}

function toPayload(draft: Draft): CalendarSettings {
  return {
    ...draftContent(draft),
    bufferMinutes: Number(draft.bufferMinutes),
  };
}

function ErrorText({ id, message }: { id: string; message?: string }) {
  return message ? (
    <p id={id} className="text-sm text-destructive">{message}</p>
  ) : null;
}

export default function CalendarSettingsPage() {
  const { userId, isAuthenticated, roles } = useAuthStore();

  if (!isAuthenticated || !roles.includes('Therapist') || !userId) return null;

  return <SettingsContent key={userId} accountId={userId} />;
}

function SettingsContent({ accountId }: { accountId: string }) {
  const query = useQuery({
    queryKey: calendarSettingsKey(accountId),
    queryFn: ({ signal }) => calendarApi.settings(signal),
    staleTime: 0,
    refetchOnWindowFocus: false,
  });

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 sm:p-6">
      <header>
        <p className="text-xs font-semibold uppercase tracking-widest text-primary">
          Organizacja pracy
        </p>
        <h1 className="mt-3 text-4xl sm:text-5xl">Kalendarz i wizyty</h1>
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          Godziny pracy, długości wizyt i bufor. Strefa Europe/Warsaw.
        </p>
      </header>

      {query.isPending ? (
        <p role="status">Wczytywanie ustawień…</p>
      ) : query.isError ? (
        <div className="space-y-4 rounded-xl border bg-card p-5">
          <p role="alert" className="text-sm text-destructive">
            {getCalendarError(query.error)}
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
      ) : (
        <SettingsForm accountId={accountId} initial={query.data} />
      )}
    </div>
  );
}

function SettingsForm({
  accountId,
  initial,
}: {
  accountId: string;
  initial: CalendarSettings;
}) {
  const queryClient = useQueryClient();
  const nextId = useRef(0);

  const [base, setBase] = useState(() => makeDraft(initial));
  const [draft, setDraft] = useState(() => makeDraft(initial));
  const [errors, setErrors] = useState<CalendarFormErrors>({});
  const [saved, setSaved] = useState(false);

  const dirty =
    JSON.stringify(draftContent(draft)) !==
    JSON.stringify(draftContent(base));

  const mutation = useMutation({
    mutationFn: calendarApi.updateSettings,
    retry: false,
    onSuccess: (data) => {
      const next = makeDraft(data);
      setBase(next);
      setDraft(next);
      setErrors({});
      setSaved(true);
      queryClient.setQueryData(calendarSettingsKey(accountId), data);
    },
    onError: (error) => {
      setErrors(getCalendarFieldErrors(error));
      setSaved(false);
    },
  });

  const { confirmLeave, discardDialog } = useUnsavedChanges(
    dirty,
    mutation.isPending,
  );

  function change(next: Draft) {
    setDraft(next);
    setErrors({});
    setSaved(false);
    mutation.reset();
  }

  function changeDay(index: number, intervals: CalendarInterval[]) {
    change({
      ...draft,
      weeklyHours: draft.weeklyHours.map((day, current) =>
        current === index ? { ...day, intervals } : day,
      ),
    });
  }

  function changeException(
    index: number,
    patch: Partial<Draft['exceptions'][number]>,
  ) {
    change({
      ...draft,
      exceptions: draft.exceptions.map((exception, current) =>
        current === index ? { ...exception, ...patch } : exception,
      ),
    });
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mutation.isPending) return;

    const nextErrors = validateCalendarSettings(draft);
    setErrors(nextErrors);
    setSaved(false);

    if (Object.keys(nextErrors).length === 0) {
      mutation.mutate(toPayload(draft));
    }
  }

  return (
    <>
      <form noValidate onSubmit={submit} className="space-y-6">
        <aside className="rounded-xl border bg-primary/5 p-5 text-sm leading-relaxed">
          Zmiana ustawień wpływa na nowe terminy. Nie przesuwa ani nie
          usuwa wcześniej zapisanych wizyt.
        </aside>

        <fieldset disabled={mutation.isPending} className="min-w-0 space-y-6">
          <section className="space-y-5 rounded-2xl border bg-card p-5 sm:p-6">
            <h2 className="text-3xl">Długość wizyt i bufor</h2>

            <fieldset aria-describedby="duration-error">
              <legend className="mb-3 text-sm font-medium">
                Dozwolone długości wizyt
              </legend>
              <div className="flex flex-wrap gap-3">
                {[30, 45, 60].map((duration) => (
                  <label
                    key={duration}
                    className="flex items-center gap-2 rounded-lg border px-4 py-3 text-sm"
                  >
                    <input
                      type="checkbox"
                      checked={draft.allowedDurations.includes(duration)}
                      onChange={(event) =>
                        change({
                          ...draft,
                          allowedDurations: event.target.checked
                            ? [...draft.allowedDurations, duration]
                            : draft.allowedDurations.filter((v) => v !== duration),
                        })
                      }
                      className="h-4 w-4 accent-teal-600"
                    />
                    {duration} minut
                  </label>
                ))}
              </div>
              <div className="mt-2">
                <ErrorText id="duration-error" message={errors.allowedDurations} />
              </div>
            </fieldset>

            <div className="max-w-sm space-y-2">
              <Label htmlFor="calendar-buffer">Bufor po wizycie — minuty</Label>
              <Input
                id="calendar-buffer"
                type="number"
                min={0}
                max={60}
                step={1}
                value={draft.bufferMinutes}
                onChange={(event) =>
                  change({ ...draft, bufferMinutes: event.target.value })
                }
                aria-invalid={Boolean(errors.bufferMinutes)}
                aria-describedby="buffer-error"
              />
              <ErrorText id="buffer-error" message={errors.bufferMinutes} />
              <p className="text-xs text-muted-foreground">
                Od 0 do 60 minut. Bufor również musi zmieścić się w dostępności.
              </p>
            </div>
          </section>

          <section className="space-y-5 rounded-2xl border bg-card p-5 sm:p-6">
            <div>
              <h2 className="text-3xl">Tygodniowe godziny pracy</h2>
              <p className="mt-2 text-sm text-muted-foreground">
                Brak przedziałów oznacza dzień wolny. Przerwy ustawiasz,
                dodając osobne przedziały.
              </p>
            </div>

            <ErrorText id="weekly-error" message={errors.weeklyHours} />

            {draft.weeklyHours.map((day, index) => (
              <article key={day.dayOfWeek} className="rounded-xl border p-4">
                <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                  <h3 className="text-2xl">{CALENDAR_DAYS[index].label}</h3>
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={day.intervals.length === 0}
                      onChange={(event) =>
                        changeDay(
                          index,
                          event.target.checked
                            ? []
                            : [{ start: '09:00', end: '17:00' }],
                        )
                      }
                      className="h-4 w-4 accent-teal-600"
                    />
                    Dzień wolny
                  </label>
                </div>

                <Intervals
                  id={`weekly-${index}`}
                  label={CALENDAR_DAYS[index].label}
                  value={day.intervals}
                  error={errors[`weeklyHours[${index}].intervals`]}
                  onChange={(intervals) => changeDay(index, intervals)}
                />
              </article>
            ))}
          </section>

          <section className="space-y-5 rounded-2xl border bg-card p-5 sm:p-6">
            <div>
              <h2 className="text-3xl">Wyjątki dla konkretnych dat</h2>
              <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
                Wyjątek zastępuje cały plan wybranego dnia.
                Po usunięciu wyjątku wraca plan tygodniowy.
              </p>
            </div>

            <ErrorText id="exceptions-error" message={errors.exceptions} />

            {draft.exceptions.length === 0 && (
              <p className="rounded-xl border border-dashed p-5 text-sm text-muted-foreground">
                Nie dodano wyjątków.
              </p>
            )}

            {draft.exceptions.map((exception, index) => {
              const prefix = `exceptions[${index}]`;
              const id = `exception-${exception.localId}`;

              return (
                <article key={exception.localId} className="space-y-4 rounded-xl border p-4">
                  <div className="flex flex-wrap items-end justify-between gap-3">
                    <div className="space-y-2">
                      <Label htmlFor={`${id}-date`}>Data wyjątku</Label>
                      <Input
                        id={`${id}-date`}
                        type="date"
                        value={exception.date}
                        onChange={(event) =>
                          changeException(index, { date: event.target.value })
                        }
                        aria-invalid={Boolean(errors[`${prefix}.date`])}
                        aria-describedby={`${id}-date-error`}
                      />
                      <ErrorText
                        id={`${id}-date-error`}
                        message={errors[`${prefix}.date`]}
                      />
                    </div>

                    <Button
                      type="button"
                      variant="outline"
                      onClick={() =>
                        change({
                          ...draft,
                          exceptions: draft.exceptions.filter((_, i) => i !== index),
                        })
                      }
                    >
                      Usuń wyjątek
                    </Button>
                  </div>

                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={exception.isDayOff}
                      onChange={(event) =>
                        changeException(index, {
                          isDayOff: event.target.checked,
                          intervals: event.target.checked
                            ? []
                            : [{ start: '09:00', end: '17:00' }],
                        })
                      }
                      className="h-4 w-4 accent-teal-600"
                    />
                    Cały dzień wolny
                  </label>

                  {!exception.isDayOff && (
                    <Intervals
                      id={id}
                      label={`Wyjątek ${exception.date || index + 1}`}
                      value={exception.intervals}
                      error={errors[`${prefix}.intervals`]}
                      onChange={(intervals) =>
                        changeException(index, { intervals })
                      }
                    />
                  )}

                  <ErrorText id={`${id}-error`} message={errors[prefix]} />
                </article>
              );
            })}

            <Button
              type="button"
              variant="outline"
              disabled={draft.exceptions.length >= 366}
              onClick={() => {
                const localId = `new-${nextId.current++}`;
                change({
                  ...draft,
                  exceptions: [
                    ...draft.exceptions,
                    { localId, date: '', isDayOff: true, intervals: [] },
                  ],
                });
              }}
            >
              Dodaj wyjątek
            </Button>
          </section>

          {Object.keys(errors).length > 0 && (
            <div role="alert" className="rounded-xl border border-destructive/30 p-5 text-sm text-destructive">
              <p className="font-medium">Popraw zaznaczone pola:</p>
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

          {saved && !dirty && (
            <p role="status" className="rounded-xl bg-primary/10 p-4 text-sm text-primary">
              Ustawienia zostały zapisane.
            </p>
          )}

          <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border bg-card p-5">
            <p role="status" className="text-sm text-muted-foreground">
              {dirty ? 'Masz niezapisane zmiany.' : 'Brak niezapisanych zmian.'}
            </p>
            <div className="flex flex-wrap gap-3">
              <Button
                type="button"
                variant="outline"
                disabled={!dirty}
                onClick={() =>
                  confirmLeave(() => {
                    setDraft(base);
                    setErrors({});
                    setSaved(false);
                    mutation.reset();
                  })
                }
              >
                Cofnij zmiany
              </Button>
              <Button type="submit" disabled={!dirty || mutation.isPending}>
                {mutation.isPending ? 'Zapisywanie…' : 'Zapisz ustawienia'}
              </Button>
            </div>
          </div>
        </fieldset>
      </form>

      {discardDialog}
    </>
  );
}

function Intervals({
  id,
  label,
  value,
  error,
  onChange,
}: {
  id: string;
  label: string;
  value: CalendarInterval[];
  error?: string;
  onChange: (value: CalendarInterval[]) => void;
}) {
  return (
    <div className="space-y-3">
      {value.length === 0 && (
        <p className="text-sm text-muted-foreground">Brak dostępnych godzin.</p>
      )}

      {value.map((interval, index) => (
        <div key={`${id}-${index}`} className="grid grid-cols-2 items-end gap-3 sm:grid-cols-[1fr_1fr_auto]">
          {(['start', 'end'] as const).map((field) => (
            <div key={field} className="space-y-2">
              <Label htmlFor={`${id}-${index}-${field}`}>
                {field === 'start' ? 'Od' : 'Do'}
                <span className="sr-only">: {label}, przedział {index + 1}</span>
              </Label>
              <Input
                id={`${id}-${index}-${field}`}
                type="time"
                step={60}
                value={interval[field]}
                aria-invalid={Boolean(error)}
                aria-describedby={`${id}-error`}
                onChange={(event) =>
                  onChange(
                    value.map((item, i) =>
                      i === index ? { ...item, [field]: event.target.value } : item,
                    ),
                  )
                }
              />
            </div>
          ))}
          <Button
            type="button"
            variant="outline"
            className="col-span-2 sm:col-span-1"
            aria-label={`Usuń przedział ${index + 1}: ${label}`}
            onClick={() => onChange(value.filter((_, i) => i !== index))}
          >
            Usuń
          </Button>
        </div>
      ))}

      <ErrorText id={`${id}-error`} message={error} />

      <Button
        type="button"
        variant="outline"
        disabled={value.length >= 8}
        onClick={() => onChange([...value, { start: '09:00', end: '17:00' }])}
      >
        Dodaj przedział
      </Button>
    </div>
  );
}