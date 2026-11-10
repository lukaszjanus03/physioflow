'use client';

import { addDays, format, parse } from 'date-fns';
import { pl } from 'date-fns/locale';
import { ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react';
import { useId, useState } from 'react';
import './appointment-booking-layout.css';

import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import {
  appointmentDateTime,
  appointmentTime,
  toCalendarDate,
  warsawDateKey,
} from '@/lib/appointment-display';
import type { AvailableSlot } from '@/lib/appointments-api';
import { isCalendarDate } from '@/lib/calendar-validation';

type Period = 'all' | 'morning' | 'afternoon' | 'evening';

const periods: { value: Period; label: string }[] = [
  { value: 'all', label: 'Wszystkie' },
  { value: 'morning', label: 'Rano' },
  { value: 'afternoon', label: 'Popołudnie' },
  { value: 'evening', label: 'Wieczór' },
];

function parseDay(value: string) {
  return parse(value, 'yyyy-MM-dd', new Date(2000, 0, 1));
}

function dateKey(value: Date) {
  return format(value, 'yyyy-MM-dd');
}

export default function AppointmentSlotSelector({
  date,
  onDateChange,
  duration,
  durations,
  onDurationChange,
  slots,
  value,
  onChange,
  now,
  loading,
  error,
  fieldErrors = {},
  onRefresh,
  disabled = false,
  currentStartsAt,
}: {
  date: string;
  onDateChange: (date: string) => void;
  duration: number;
  durations: number[];
  onDurationChange: (duration: number) => void;
  slots: AvailableSlot[];
  value: string;
  onChange: (startsAt: string) => void;
  now: number;
  loading: boolean;
  error?: string;
  fieldErrors?: Record<string, string>;
  onRefresh: () => void;
  disabled?: boolean;
  currentStartsAt?: string;
}) {
  const id = useId();
  const today = warsawDateKey(new Date(now));

  const [windowStart, setWindowStart] = useState(() =>
    isCalendarDate(date) && date >= today ? date : today,
  );
  const [period, setPeriod] = useState<Period>('all');

  const effectiveStart = windowStart < today ? today : windowStart;
  const firstDay = parseDay(effectiveStart);
  const days = Array.from({ length: 7 }, (_, index) =>
    addDays(firstDay, index),
  );

  const options = slots
    .filter(
      (slot) =>
        new Date(slot.startsAt).getTime() > now &&
        warsawDateKey(slot.startsAt) === date,
    )
    .sort(
      (a, b) =>
        new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime(),
    );

  const selected = options.find(
    (slot) =>
      new Date(slot.startsAt).getTime() === new Date(value).getTime(),
  );

  const filtered = options.filter((slot) => {
    if (period === 'all') return true;

    const hour = toCalendarDate(slot.startsAt).getHours();

    if (period === 'morning') return hour < 12;
    if (period === 'afternoon') return hour >= 12 && hour < 17;

    return hour >= 17;
  });

  function moveDays(direction: -1 | 1) {
    const proposed = dateKey(addDays(firstDay, direction * 7));
    const next = proposed < today ? today : proposed;

    setWindowStart(next);
    onDateChange(next);
  }

  return (
    <section className="appointment-slot-selector overflow-hidden rounded-2xl border bg-card">
      <input
        type="hidden"
        name="startsAt"
        value={!loading && !error && selected ? selected.startsAt : ''}
      />

      <fieldset disabled={disabled} className="min-w-0">
        <div className="space-y-5 border-b p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-3xl">Wybierz termin</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Godziny w strefie Europe/Warsaw.
              </p>
            </div>
            <span className="text-sm font-medium capitalize">
              {format(firstDay, 'LLLL yyyy', { locale: pl })}
            </span>
          </div>

          <fieldset className="space-y-3">
            <legend className="mb-3 text-sm font-medium">
              Długość wizyty
            </legend>

            <div className="flex flex-wrap gap-2">
              {durations.map((minutes) => (
                <label key={minutes} className="cursor-pointer">
                  <input
                    type="radio"
                    name={`${id}-duration`}
                    value={minutes}
                    checked={duration === minutes}
                    onChange={() => onDurationChange(minutes)}
                    className="peer sr-only"
                  />
                  <span
                    className={`block rounded-xl border px-5 py-3 text-sm transition-colors peer-focus-visible:ring-2 peer-focus-visible:ring-primary ${
                      duration === minutes
                        ? 'border-primary bg-primary text-primary-foreground'
                        : 'bg-background hover:bg-muted/50'
                    }`}
                  >
                    {minutes} minut
                  </span>
                </label>
              ))}
            </div>

            {fieldErrors.durationMinutes && (
              <p role="alert" className="text-sm text-destructive">
                {fieldErrors.durationMinutes}
              </p>
            )}
          </fieldset>

          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="shrink-0 rounded-full"
              aria-label="Poprzednie siedem dni"
              disabled={effectiveStart <= today}
              onClick={() => moveDays(-1)}
            >
              <ChevronLeft aria-hidden="true" className="h-4 w-4" />
            </Button>

            <div
              className="flex min-w-0 flex-1 gap-2 overflow-x-auto pb-2"
              aria-label="Wybierz dzień wizyty"
            >
              {days.map((day) => {
                const key = dateKey(day);
                const active = key === date;

                return (
                  <button
                    key={key}
                    type="button"
                    aria-pressed={active}
                    aria-label={format(day, 'EEEE, dd MMMM yyyy', { locale: pl })}
                    onClick={() => onDateChange(key)}
                    className={`flex min-h-24 min-w-[70px] flex-1 flex-col items-center justify-center gap-2 rounded-xl border px-3 py-3 text-center transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                      active
                        ? 'border-primary bg-primary text-primary-foreground shadow-sm'
                        : 'bg-background hover:border-primary/50 hover:bg-primary/5'
                    }`}
                  >
                    <span className="text-xs capitalize">
                      {format(day, 'EEE', { locale: pl })}
                    </span>
                    <span className="text-xl font-semibold">
                      {format(day, 'dd')}
                    </span>
                    <span
                      aria-hidden="true"
                      className={`h-1 w-5 rounded-full ${
                        active ? 'bg-primary-foreground' : 'bg-transparent'
                      }`}
                    />
                  </button>
                );
              })}
            </div>

            <Button
              type="button"
              variant="outline"
              size="icon"
              className="shrink-0 rounded-full"
              aria-label="Następne siedem dni"
              onClick={() => moveDays(1)}
            >
              <ChevronRight aria-hidden="true" className="h-4 w-4" />
            </Button>
          </div>

          <div className="flex flex-wrap items-end justify-between gap-3">
            <div className="space-y-2">
              <Label htmlFor={`${id}-date`}>Lub wybierz datę</Label>
              <Input
                id={`${id}-date`}
                type="date"
                value={date}
                min={today}
                aria-invalid={Boolean(fieldErrors.date)}
                aria-describedby={`${id}-date-error`}
                onChange={(event) => {
                  const next = event.target.value;
                  onDateChange(next);

                  if (isCalendarDate(next) && next >= today) {
                    setWindowStart(next);
                  }
                }}
              />
            </div>

            <Button
              type="button"
              variant="outline"
              disabled={loading || !isCalendarDate(date)}
              onClick={onRefresh}
            >
              <RefreshCw
                aria-hidden="true"
                className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`}
              />
              Odśwież godziny
            </Button>
          </div>

          {fieldErrors.date && (
            <p id={`${id}-date-error`} className="text-sm text-destructive">
              {fieldErrors.date}
            </p>
          )}
        </div>

        <div className="space-y-5 p-4 sm:p-5">
          <div
            className="flex flex-wrap gap-1 rounded-xl bg-muted/60 p-1"
            aria-label="Pora dnia"
          >
            {periods.map((item) => (
              <button
                key={item.value}
                type="button"
                aria-pressed={period === item.value}
                onClick={() => setPeriod(item.value)}
                className={`min-h-10 flex-1 rounded-lg px-3 py-2 text-xs transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                  period === item.value
                    ? 'bg-background font-medium text-primary shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                }`}
              >
                {item.label}
              </button>
            ))}
          </div>

          {!isCalendarDate(date) ? (
            <p className="text-sm text-muted-foreground">
              Wybierz poprawną datę.
            </p>
          ) : loading ? (
            <p role="status" className="py-5 text-center text-sm text-muted-foreground">
              Sprawdzanie dostępnych godzin…
            </p>
          ) : error ? (
            <p role="alert" className="text-sm text-destructive">{error}</p>
          ) : options.length === 0 ? (
            <div className="rounded-xl border border-dashed p-6 text-center">
              <p className="text-sm font-medium">Brak wolnych terminów</p>
              <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
                Wybierz inny dzień lub inną długość wizyty.
              </p>
            </div>
          ) : filtered.length === 0 ? (
            <p role="status" className="py-4 text-center text-sm text-muted-foreground">
              Brak godzin dla tej pory dnia. Wybierz inną porę lub „Wszystkie”.
            </p>
          ) : (
            <div
              className="grid max-h-64 grid-cols-3 gap-2 overflow-y-auto sm:grid-cols-4"
              aria-label="Dostępne godziny"
            >
              {filtered.map((slot) => {
                const active = selected?.startsAt === slot.startsAt;
                const current =
                  currentStartsAt &&
                  new Date(currentStartsAt).getTime() ===
                    new Date(slot.startsAt).getTime();

                return (
                  <button
                    key={slot.startsAt}
                    type="button"
                    aria-pressed={active}
                    aria-label={`${appointmentTime(slot.startsAt)} do ${appointmentTime(slot.endsAt)}${current ? ', obecny termin' : ''}`}
                    onClick={() => onChange(slot.startsAt)}
                    className={`min-h-14 rounded-xl border px-2 py-3 text-center transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                      active
                        ? 'border-primary bg-primary text-primary-foreground shadow-sm'
                        : 'bg-background hover:border-primary/50 hover:bg-primary/5'
                    }`}
                  >
                    <span className="block text-sm font-semibold">
                      {appointmentTime(slot.startsAt)}
                    </span>
                    {current && <span className="mt-1 block text-[10px]">Obecny</span>}
                  </button>
                );
              })}
            </div>
          )}

          {fieldErrors.startsAt && (
            <p role="alert" className="text-sm text-destructive">
              {fieldErrors.startsAt}
            </p>
          )}

          {selected && !error && !loading && (
            <div className="rounded-xl border border-primary/20 bg-primary/5 p-4">
              <p className="text-xs font-medium uppercase tracking-wider text-primary">
                Wybrany termin
              </p>
              <p className="mt-2 text-sm font-semibold">
                {appointmentDateTime(selected.startsAt)}
              </p>
              <p className="mt-2 text-xs text-muted-foreground">
                Do {appointmentTime(selected.endsAt)} · {duration} minut
              </p>
            </div>
          )}

          <p className="text-xs leading-relaxed text-muted-foreground">
            Dostępność sprawdzamy dla wybranego dnia. Termin zostanie
            ponownie zweryfikowany podczas zapisu.
          </p>
        </div>
      </fieldset>
    </section>
  );
}