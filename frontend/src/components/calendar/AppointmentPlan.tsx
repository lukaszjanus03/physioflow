'use client';

import { addDays, format } from 'date-fns';
import { pl } from 'date-fns/locale';
import { CalendarDays, ChevronRight, Clock, Plus } from 'lucide-react';
import { useId } from 'react';

import { Button } from '@/components/ui/button';
import { useCurrentTime } from '@/hooks/use-current-time';
import {
  APPOINTMENT_COLORS,
  APPOINTMENT_LABELS,
  appointmentTime,
  calendarDateKey,
  toCalendarDate,
  warsawDateKey,
} from '@/lib/appointment-display';
import type { Appointment, AppointmentStatus } from '@/types/api';

const statusStyles: Record<AppointmentStatus, string> = {
  Scheduled: 'bg-teal-50 text-teal-800',
  Completed: 'bg-blue-50 text-blue-800',
  Cancelled: 'bg-slate-100 text-slate-700',
  NoShow: 'bg-rose-50 text-rose-800',
};

const pluralRules = new Intl.PluralRules('pl');

function appointmentCount(count: number) {
  const category = pluralRules.select(count);

  if (category === 'one') return `${count} wizyta`;
  if (category === 'few') return `${count} wizyty`;

  return `${count} wizyt`;
}

export default function AppointmentPlan({
  firstDay,
  daysCount,
  appointments,
  canCreate,
  canOpen,
  onCreate,
  onOpen,
}: {
  firstDay: Date;
  daysCount: number;
  appointments: Appointment[];
  canCreate: boolean;
  canOpen: boolean;
  onCreate: (date: Date) => void;
  onOpen: (id: string) => void;
}) {
  const id = useId();
  const now = useCurrentTime();
  const today = warsawDateKey(new Date(now));

  const days = Array.from({ length: daysCount }, (_, index) =>
    addDays(firstDay, index),
  );

  const lastDay = days[days.length - 1];

  if (appointments.length === 0) {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center rounded-2xl border border-dashed bg-muted/20 px-5 py-10 text-center">
        <span className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <CalendarDays aria-hidden="true" className="h-7 w-7" />
        </span>

        <h3 className="mt-5 text-3xl">Brak wizyt do pokazania</h3>

        <p className="mt-2 max-w-md text-sm leading-relaxed text-muted-foreground">
          W wybranym okresie i przy aktualnych filtrach nie ma wizyt.
          Możesz zmienić datę lub dodać nową wizytę.
        </p>

        {canCreate && calendarDateKey(lastDay) >= today && (
          <Button
            type="button"
            className="mt-5"
            onClick={() =>
              onCreate(
                calendarDateKey(firstDay) < today
                  ? toCalendarDate(new Date(now))
                  : firstDay,
              )
            }
          >
            <Plus aria-hidden="true" className="mr-2 h-4 w-4" />
            Dodaj wizytę
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {days.map((day) => {
        const key = calendarDateKey(day);
        const isToday = key === today;

        const items = appointments
          .filter((appointment) => warsawDateKey(appointment.startsAt) === key)
          .sort(
            (a, b) =>
              new Date(a.startsAt).getTime() -
              new Date(b.startsAt).getTime(),
          );

        const headingId = `${id}-${key}`;

        return (
          <section
            key={key}
            aria-labelledby={headingId}
            className={`overflow-hidden rounded-2xl border ${
              isToday ? 'border-primary/30 bg-primary/[0.025]' : 'bg-card'
            }`}
          >
            <div className="flex flex-wrap items-center justify-between gap-3 border-b bg-muted/20 px-4 py-3 sm:px-5">
              <div className="flex items-center gap-3">
                <div
                  aria-hidden="true"
                  className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl text-xl font-semibold tabular-nums ${
                    isToday
                      ? 'bg-primary text-primary-foreground'
                      : 'border bg-background'
                  }`}
                >
                  {format(day, 'dd')}
                </div>

                <div>
                  <h3
                    id={headingId}
                    className="text-xl capitalize text-foreground"
                  >
                    {format(day, 'EEEE', { locale: pl })}
                  </h3>

                  <p className="mt-1 text-xs text-muted-foreground">
                    {format(day, 'd MMMM yyyy', { locale: pl })}
                  </p>
                </div>

                {isToday && (
                  <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
                    Dzisiaj
                  </span>
                )}
              </div>

              <div className="flex items-center gap-3">
                <span className="text-xs text-muted-foreground">
                  {appointmentCount(items.length)}
                </span>

                {canCreate && key >= today && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => onCreate(day)}
                    aria-label={`Dodaj wizytę: ${format(day, 'dd.MM.yyyy')}`}
                  >
                    <Plus aria-hidden="true" className="mr-1 h-4 w-4" />
                    Dodaj
                  </Button>
                )}
              </div>
            </div>

            {items.length === 0 ? (
              <p className="px-4 py-4 text-sm text-muted-foreground sm:px-5">
                Brak wizyt w tym widoku.
              </p>
            ) : (
              <div className="grid gap-3 p-4 md:grid-cols-2 xl:grid-cols-3 sm:p-5">
                {items.map((appointment) => {
                  const ongoing =
                    appointment.status === 'Scheduled' &&
                    new Date(appointment.startsAt).getTime() <= now &&
                    new Date(appointment.endsAt).getTime() > now;

                  return (
                    <button
                      key={appointment.id}
                      type="button"
                      disabled={!canOpen}
                      onClick={() => onOpen(appointment.id)}
                      aria-label={`${appointment.patientName}, ${appointmentTime(appointment.startsAt)} do ${appointmentTime(appointment.endsAt)}, ${APPOINTMENT_LABELS[appointment.status]}. Otwórz szczegóły wizyty.`}
                      className="group flex min-w-0 flex-col rounded-xl border border-l-4 bg-background p-4 text-left shadow-sm transition-colors hover:border-primary/50 hover:bg-primary/[0.025] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
                      style={{
                        borderLeftColor:
                          APPOINTMENT_COLORS[appointment.status],
                      }}
                    >
                      <span className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-lg font-semibold tabular-nums">
                          {appointmentTime(appointment.startsAt)}
                          {' – '}
                          {appointmentTime(appointment.endsAt)}
                        </span>

                        <span
                          className={`rounded-full px-2.5 py-1 text-[11px] font-medium ${statusStyles[appointment.status]}`}
                        >
                          {APPOINTMENT_LABELS[appointment.status]}
                        </span>
                      </span>

                      <span className="mt-4 break-words text-base font-semibold">
                        {appointment.patientName}
                      </span>

                      <span className="mt-2 flex items-center gap-2 text-xs text-muted-foreground">
                        <Clock aria-hidden="true" className="h-3.5 w-3.5" />
                        {appointment.durationMinutes} minut
                      </span>

                      <span className="mt-5 flex items-center justify-between gap-3 border-t pt-3">
                        <span className="text-xs text-primary">
                          {ongoing ? 'Wizyta w trakcie' : 'Szczegóły wizyty'}
                        </span>

                        <ChevronRight
                          aria-hidden="true"
                          className="h-4 w-4 shrink-0 text-primary"
                        />
                      </span>
                    </button>
                  );
                })}
              </div>
            )}
          </section>
        );
      })}
    </div>
  );
}