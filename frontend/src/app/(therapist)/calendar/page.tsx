'use client';

import { useQuery } from '@tanstack/react-query';
import { addDays, format, getDay, parse, startOfWeek } from 'date-fns';
import { pl } from 'date-fns/locale';
import Link from 'next/link';
import { useState, useSyncExternalStore } from 'react';
import { Calendar, dateFnsLocalizer } from 'react-big-calendar';
import type { EventProps, HeaderProps } from 'react-big-calendar';

import AppointmentDialog from '@/components/calendar/AppointmentDialog';
import type { AppointmentSelection } from '@/components/calendar/AppointmentDialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/primitives';
import {
  APPOINTMENT_COLORS,
  APPOINTMENT_LABELS,
  appointmentDateTime,
  appointmentTime,
  calendarDateKey,
  dayIntervals,
  toCalendarDate,
} from '@/lib/appointment-display';
import { appointmentsApi } from '@/lib/appointments-api';
import {
  calendarApi,
  calendarSettingsKey,
  getCalendarError,
} from '@/lib/calendar-api';
import { useAuthStore } from '@/stores/auth-store';
import type { Appointment } from '@/types/api';

import 'react-big-calendar/lib/css/react-big-calendar.css';
import './calendar.css';

type CalendarView = 'day' | 'week';

type CalendarEvent = {
  id: string;
  title: string;
  start: Date;
  end: Date;
  appointment: Appointment;
};

const localizer = dateFnsLocalizer({
  format,
  parse,
  startOfWeek: (date: Date) => startOfWeek(date, { weekStartsOn: 1 }),
  getDay,
  locales: { pl },
});

function subscribeReady() { return () => {}; }
function clientReady() { return true; }
function serverReady() { return false; }

function subscribeMobile(callback: () => void) {
  const media = window.matchMedia('(max-width: 767px)');
  media.addEventListener('change', callback);
  return () => media.removeEventListener('change', callback);
}

function mobileSnapshot() {
  return window.matchMedia('(max-width: 767px)').matches;
}

function minutes(value: string) {
  const [hours, mins] = value.split(':').map(Number);
  return hours * 60 + mins;
}

function CalendarHeader({ date }: HeaderProps) {
  return (
    <div className="calendar-day-heading">
      <span>{format(date, 'dd.MM.yyyy')}</span>
      <span>{format(date, 'EEEE', { locale: pl })}</span>
    </div>
  );
}

function EventContent({ event }: EventProps<CalendarEvent>) {
  return (
    <div className="calendar-appointment">
      <span className="calendar-appointment-time">
        {appointmentTime(event.appointment.startsAt)}
        {' – '}{appointmentTime(event.appointment.endsAt)}
      </span>
      <strong className="calendar-appointment-name">
        {event.appointment.patientName}
      </strong>
      <span className="calendar-appointment-status">
        {APPOINTMENT_LABELS[event.appointment.status]}
      </span>
    </div>
  );
}

export default function CalendarPage() {
  const ready = useSyncExternalStore(subscribeReady, clientReady, serverReady);
  const { userId, isAuthenticated, roles } = useAuthStore();

  if (!ready) return <p role="status" className="p-6">Wczytywanie kalendarza…</p>;
  if (!isAuthenticated || !roles.includes('Therapist') || !userId) return null;

  return <TherapistCalendar key={userId} accountId={userId} />;
}

function TherapistCalendar({ accountId }: { accountId: string }) {
  const mobile = useSyncExternalStore(
    subscribeMobile,
    mobileSnapshot,
    serverReady,
  );

  const [date, setDate] = useState(() => toCalendarDate(new Date()));
  const [chosenView, setChosenView] = useState<CalendarView | null>(null);
  const [selection, setSelection] = useState<AppointmentSelection | null>(null);
  const [notice, setNotice] = useState('');
  const [showCancelled, setShowCancelled] = useState(false);
  const [fullDay, setFullDay] = useState(false);

  const view = chosenView ?? (mobile ? 'day' : 'week');
  const first = view === 'week' ? startOfWeek(date, { weekStartsOn: 1 }) : date;
  const last = view === 'week' ? addDays(first, 6) : first;
  const from = calendarDateKey(first);
  const to = calendarDateKey(last);

  const settings = useQuery({
    queryKey: calendarSettingsKey(accountId),
    queryFn: ({ signal }) => calendarApi.settings(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const appointments = useQuery({
    queryKey: ['therapist-appointments', accountId, 'list', from, to],
    queryFn: ({ signal }) => appointmentsApi.list(from, to, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const allAppointments = appointments.data ?? [];
  const visible = allAppointments.filter(
    (appointment) => showCancelled || appointment.status !== 'Cancelled',
  );

  const events: CalendarEvent[] = visible.map((appointment) => ({
    id: appointment.id,
    title:
      `${appointment.patientName} | ` +
      `${appointmentTime(appointment.startsAt)}–${appointmentTime(appointment.endsAt)} | ` +
      APPOINTMENT_LABELS[appointment.status],
    start: toCalendarDate(appointment.startsAt),
    end: toCalendarDate(appointment.endsAt),
    appointment,
  }));

  const configured = Boolean(
    settings.data?.weeklyHours.some((day) => day.intervals.length > 0) ||
    settings.data?.exceptions.some(
      (exception) => !exception.isDayOff && exception.intervals.length > 0,
    ),
  );

  const boundaries: number[] = [];

  if (settings.data) {
    for (let offset = 0; offset < (view === 'week' ? 7 : 1); offset += 1) {
      for (const interval of dayIntervals(settings.data, addDays(first, offset))) {
        boundaries.push(minutes(interval.start), minutes(interval.end));
      }
    }
  }

  for (const event of events) {
    boundaries.push(event.start.getHours() * 60 + event.start.getMinutes());

    boundaries.push(
      calendarDateKey(event.end) !== calendarDateKey(event.start)
        ? 1440
        : event.end.getHours() * 60 + event.end.getMinutes(),
    );
  }

  const startHour = fullDay
    ? 0
    : Math.max(0, Math.floor(Math.min(...(boundaries.length ? boundaries : [480])) / 60) - 1);

  const endHour = fullDay
    ? 24
    : Math.min(24, Math.ceil(Math.max(...(boundaries.length ? boundaries : [1080])) / 60) + 1);

  const minTime = new Date(2000, 0, 1, startHour, 0);
  const maxTime = new Date(
    2000, 0, 1,
    endHour === 24 ? 23 : endHour,
    endHour === 24 ? 59 : 0,
  );

  const heading = view === 'day'
    ? format(date, 'EEEE, dd MMMM yyyy', { locale: pl })
    : `${format(first, 'dd.MM.yyyy')} – ${format(last, 'dd.MM.yyyy')}`;

  function openNew(day: Date) {
    setNotice('');
    setSelection({ kind: 'new', date: calendarDateKey(day) });
  }

  function openAppointment(id: string) {
    setNotice('');
    setSelection({ kind: 'existing', id });
  }

  return (
    <div className="space-y-6 p-4 sm:p-6 lg:p-8">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-widest text-primary">
            Organizacja gabinetu
          </p>
          <h1 className="mt-3 text-4xl sm:text-5xl">Kalendarz wizyt</h1>
          <p className="mt-3 text-sm text-muted-foreground">
            Terminy w strefie Europe/Warsaw.
          </p>
        </div>

        <div className="flex flex-wrap gap-3">
          <Button asChild variant="outline">
            <Link href="/settings/calendar">Ustawienia dostępności</Link>
          </Button>
          <Button
            type="button"
            disabled={!settings.data || settings.isError || !configured}
            onClick={() => openNew(date)}
          >
            Dodaj wizytę
          </Button>
        </div>
      </header>

      {notice && (
        <p role="status" className="rounded-xl bg-primary/10 p-4 text-sm text-primary">
          {notice}
        </p>
      )}

      {settings.isPending ? (
        <p role="status">Wczytywanie dostępności…</p>
      ) : settings.isError ? (
        <div className="space-y-3 rounded-xl border bg-card p-5">
          <p role="alert" className="text-sm text-destructive">
            {getCalendarError(settings.error)}
          </p>
          <Button type="button" variant="outline" onClick={() => void settings.refetch()}>
            Ponów pobieranie ustawień
          </Button>
        </div>
      ) : !configured ? (
        <aside className="rounded-xl border bg-amber-50 p-5 text-sm text-amber-900">
          Nie masz ustawionej dostępności.{' '}
          <Link href="/settings/calendar" className="underline">
            Ustaw godziny pracy
          </Link>
          , aby dodawać wizyty. Istniejące wizyty pozostają widoczne.
        </aside>
      ) : null}

      <section className="overflow-hidden rounded-2xl border bg-card shadow-sm">
        <div className="space-y-5 border-b bg-gradient-to-r from-primary/5 to-transparent p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <p className="text-xs text-muted-foreground">
                {view === 'week' ? 'Widok tygodniowy' : 'Widok dzienny'}
              </p>
              <h2 className="mt-1 text-3xl capitalize">{heading}</h2>
            </div>

            <div className="flex gap-2">
              {(['day', 'week'] as const).map((value) => (
                <Button
                  key={value}
                  type="button"
                  variant={view === value ? 'default' : 'outline'}
                  aria-pressed={view === value}
                  onClick={() => setChosenView(value)}
                >
                  {value === 'day' ? 'Dzień' : 'Tydzień'}
                </Button>
              ))}
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="outline"
                aria-label={view === 'week' ? 'Poprzedni tydzień' : 'Poprzedni dzień'}
                onClick={() => setDate(addDays(date, view === 'week' ? -7 : -1))}
              >
                Poprzedni
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={() => setDate(toCalendarDate(new Date()))}
              >
                Dzisiaj
              </Button>
              <Button
                type="button"
                variant="outline"
                aria-label={view === 'week' ? 'Następny tydzień' : 'Następny dzień'}
                onClick={() => setDate(addDays(date, view === 'week' ? 7 : 1))}
              >
                Następny
              </Button>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <Input
                type="date"
                aria-label="Przejdź do daty"
                className="w-auto"
                value={calendarDateKey(date)}
                onChange={(event) => {
                  if (event.target.value) {
                    setDate(parse(event.target.value, 'yyyy-MM-dd', new Date(2000, 0, 1)));
                  }
                }}
              />
              <Button
                type="button"
                variant="outline"
                disabled={appointments.isFetching}
                onClick={() => void appointments.refetch()}
              >
                {appointments.isFetching ? 'Odświeżanie…' : 'Odśwież'}
              </Button>
            </div>
          </div>

          <div className="flex flex-wrap gap-x-6 gap-y-3 text-sm">
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={showCancelled}
                onChange={(event) => setShowCancelled(event.target.checked)}
                className="h-4 w-4 accent-teal-600"
              />
              Pokaż odwołane w siatce
            </label>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={fullDay}
                onChange={(event) => setFullDay(event.target.checked)}
                className="h-4 w-4 accent-teal-600"
              />
              Pełna doba
            </label>
          </div>
        </div>

        <div className="p-4 sm:p-5">
          {appointments.isPending && (
            <p role="status" className="mb-4 text-sm">Wczytywanie wizyt…</p>
          )}
          {appointments.isError && (
            <p role="alert" className="mb-4 text-sm text-destructive">
              {getCalendarError(appointments.error)}
            </p>
          )}

          <div className={`calendar-scroll ${view === 'week' ? 'calendar-scroll-week' : ''}`}>
            <div className="physio-calendar">
              <Calendar<CalendarEvent>
                localizer={localizer}
                culture="pl"
                date={date}
                view={view}
                views={['day', 'week']}
                onNavigate={setDate}
                onView={(next) => {
                  if (next === 'day' || next === 'week') setChosenView(next);
                }}
                toolbar={false}
                events={events}
                startAccessor="start"
                endAccessor="end"
                titleAccessor="title"
                tooltipAccessor="title"
                step={30}
                timeslots={2}
                min={minTime}
                max={maxTime}
                scrollToTime={minTime}
                dayLayoutAlgorithm="no-overlap"
                getNow={() => toCalendarDate(new Date())}
                selectable={configured && Boolean(settings.data) && !settings.isError}
                onSelectSlot={(slot) => openNew(slot.start)}
                onSelectEvent={(event) => openAppointment(event.id)}
                onDrillDown={(next) => {
                  setDate(next);
                  setChosenView('day');
                }}
                eventPropGetter={(event) => ({
                  className: `calendar-event-${event.appointment.status.toLowerCase()}`,
                })}
                slotPropGetter={(slotDate) => {
                  if (!settings.data) return {};
                  const time = slotDate.getHours() * 60 + slotDate.getMinutes();
                  const available = dayIntervals(settings.data, slotDate).some(
                    (interval) =>
                      time >= minutes(interval.start) && time < minutes(interval.end),
                  );
                  return available ? {} : { className: 'calendar-unavailable-slot' };
                }}
                components={{
                  event: EventContent,
                  day: { header: CalendarHeader },
                  week: { header: CalendarHeader },
                }}
                formats={{
                  timeGutterFormat: 'HH:mm',
                  dayHeaderFormat: 'EEEE, dd.MM.yyyy',
                }}
                messages={{
                  today: 'Dzisiaj',
                  previous: 'Poprzedni',
                  next: 'Następny',
                  day: 'Dzień',
                  week: 'Tydzień',
                  allDay: 'Cały dzień',
                  noEventsInRange: 'Brak wizyt.',
                  showMore: (count) => `+${count} więcej`,
                }}
              />
            </div>
          </div>

          <div className="mt-5 flex flex-wrap gap-4 text-xs">
            {Object.entries(APPOINTMENT_LABELS).map(([status, label]) => (
              <span key={status} className="flex items-center gap-2">
                <span
                  aria-hidden="true"
                  className="h-3 w-3 rounded-full"
                  style={{
                    backgroundColor:
                      APPOINTMENT_COLORS[status as keyof typeof APPOINTMENT_COLORS],
                  }}
                />
                {label}
              </span>
            ))}
            <span className="text-muted-foreground">Szare tło: poza dostępnością</span>
          </div>

          <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
            Siatka pokazuje plan godzinowy. Dokładne dostępne terminy,
            w tym bufor i kolizje, sprawdza API w formularzu wizyty.
          </p>
        </div>
      </section>

      <section className="rounded-2xl border bg-card p-5">
        <h2 className="text-3xl">Wizyty i historia w wybranym okresie</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          Tutaj pozostają również odwołane wizyty. Każdą pozycję można
          otworzyć klawiaturą.
        </p>

        {appointments.isError ? (
          <p className="mt-4 text-sm text-destructive">
            Nie udało się pobrać listy. Użyj przycisku „Odśwież”.
          </p>
        ) : (
          <div className="mt-5 space-y-3">
            {allAppointments.map((appointment) => (
              <button
                key={appointment.id}
                type="button"
                className="flex w-full flex-wrap items-center justify-between gap-3 rounded-xl border p-4 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                onClick={() => openAppointment(appointment.id)}
              >
                <span>
                  <span className="block text-sm font-semibold">
                    {appointment.patientName}
                  </span>
                  <span className="mt-2 block text-xs text-muted-foreground">
                    {appointmentDateTime(appointment.startsAt)} ·{' '}
                    {appointment.durationMinutes} min
                  </span>
                </span>
                <span className="rounded-full bg-muted px-3 py-1 text-xs">
                  {APPOINTMENT_LABELS[appointment.status]}
                </span>
              </button>
            ))}
            {!appointments.isPending && allAppointments.length === 0 && (
              <p className="text-sm text-muted-foreground">Brak wizyt w tym okresie.</p>
            )}
          </div>
        )}
      </section>

      {selection && settings.data && (
        <AppointmentDialog
          key={selection.kind === 'existing' ? selection.id : `new-${selection.date}`}
          accountId={accountId}
          selection={selection}
          settings={settings.data}
          onClose={() => setSelection(null)}
          onSaved={(message) => {
            setNotice(message);
            setSelection(null);
          }}
        />
      )}
    </div>
  );
}