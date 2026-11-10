'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';

import { Button } from '@/components/ui/button';
import {
  appointmentDateTime,
  appointmentTime,
} from '@/lib/appointment-display';
import { getCalendarError } from '@/lib/calendar-api';
import {
  therapistAppointmentSummaryApi,
  therapistSummaryKey,
} from '@/lib/patient-appointments-api';

export function useTherapistAppointmentSummary(accountId: string) {
  return useQuery({
    queryKey: therapistSummaryKey(accountId),
    queryFn: ({ signal }) => therapistAppointmentSummaryApi(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });
}

export default function TherapistUpcomingAppointments({
  accountId,
}: {
  accountId: string;
}) {
  const query = useTherapistAppointmentSummary(accountId);

  return (
    <section className="space-y-5 rounded-2xl border bg-card p-5 sm:p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-3xl">Najbliższe wizyty</h2>
          <p className="mt-2 text-xs text-muted-foreground">
            Do 5 najbliższych zaplanowanych wizyt. Europe/Warsaw.
          </p>
        </div>
        <Button asChild variant="outline">
          <Link href="/calendar">Otwórz kalendarz</Link>
        </Button>
      </div>

      {query.isPending ? (
        <p role="status">Wczytywanie wizyt…</p>
      ) : query.isError ? (
        <div className="space-y-3">
          <p role="alert" className="text-sm text-destructive">
            {getCalendarError(query.error)}
          </p>
          <Button type="button" variant="outline" onClick={() => void query.refetch()}>
            Spróbuj ponownie
          </Button>
        </div>
      ) : query.data.upcomingAppointments.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Brak nadchodzących wizyt. Nową wizytę dodasz w kalendarzu.
        </p>
      ) : (
        <div className="divide-y rounded-xl border">
          {query.data.upcomingAppointments.map((appointment) => (
            <article
              key={appointment.id}
              className="flex flex-wrap items-center justify-between gap-3 p-4"
            >
              <div>
                <h3 className="text-xl">{appointment.patientName}</h3>
                <p className="mt-2 text-sm">
                  {appointmentDateTime(appointment.startsAt)}
                  {' – '}{appointmentTime(appointment.endsAt)}
                </p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {appointment.durationMinutes} min · Zaplanowana
                </p>
              </div>
              <Button asChild variant="outline" size="sm">
                <Link href={`/patients/${appointment.patientId}`}>
                  Karta pacjenta
                </Link>
              </Button>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}