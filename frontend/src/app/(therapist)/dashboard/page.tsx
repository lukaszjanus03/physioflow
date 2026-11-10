'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';

import TherapistUpcomingAppointments, {
  useTherapistAppointmentSummary,
} from '@/components/calendar/TherapistUpcomingAppointments';
import { Button } from '@/components/ui/button';
import { patientsApi } from '@/lib/api';
import { appointmentDateTime } from '@/lib/appointment-display';
import { getApiMessage } from '@/lib/portal-api';
import { useAuthStore } from '@/stores/auth-store';
import type { PatientListItem } from '@/types/api';

export default function DashboardPage() {
  const { userId, isAuthenticated, roles } = useAuthStore();

  if (!isAuthenticated || !roles.includes('Therapist') || !userId) return null;

  return <DashboardContent key={userId} accountId={userId} />;
}

function DashboardContent({ accountId }: { accountId: string }) {
  const active = useQuery({
    queryKey: ['dashboard', accountId, 'patients', 'active'],
    queryFn: () => patientsApi.list({ pageNumber: 1, pageSize: 5 }),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const archived = useQuery({
    queryKey: ['dashboard', accountId, 'patients', 'archived'],
    queryFn: () =>
      patientsApi.list({
        pageNumber: 1,
        pageSize: 1,
        onlyArchived: true,
      }),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const summary = useTherapistAppointmentSummary(accountId);
  const nextAppointment = summary.data?.upcomingAppointments[0];

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-4 sm:p-6 lg:p-8">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-xs font-semibold uppercase tracking-widest text-primary">
            Panel terapeuty
          </p>
          <h1 className="mt-3 text-4xl sm:text-5xl">Dashboard</h1>
          <p className="mt-3 text-sm text-muted-foreground">
            Najbliższe wizyty, pacjenci i szybki dostęp do pracy w gabinecie.
          </p>
        </div>

        <div className="flex flex-wrap gap-3">
          <Button asChild variant="outline">
            <Link href="/calendar">Kalendarz</Link>
          </Button>
          <Button asChild>
            <Link href="/patients/new">Dodaj pacjenta</Link>
          </Button>
        </div>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <Stat
          label="Aktywni pacjenci"
          value={
            active.isPending ? '…' :
            active.isError ? '—' :
            String(active.data.totalCount)
          }
          description={
            active.isError ? 'Nie udało się pobrać liczby pacjentów.' :
            'Aktywne karty pacjentów.'
          }
        />
        <Stat
          label="Archiwalni pacjenci"
          value={
            archived.isPending ? '…' :
            archived.isError ? '—' :
            String(archived.data.totalCount)
          }
          description={
            archived.isError ? 'Nie udało się pobrać archiwum.' :
            'Karty przeniesione do archiwum.'
          }
        />
        <Stat
          label="Najbliższa wizyta"
          value={
            summary.isPending ? '…' :
            summary.isError ? '—' :
            nextAppointment ? appointmentDateTime(nextAppointment.startsAt) :
            'Brak wizyt'
          }
          description={
            summary.isError ? 'Nie udało się pobrać podsumowania.' :
            nextAppointment?.patientName || 'Brak przyszłych zaplanowanych wizyt.'
          }
        />
      </div>

      <TherapistUpcomingAppointments accountId={accountId} />

      <div className="grid gap-6 xl:grid-cols-[1fr_340px]">
        <section className="rounded-2xl border bg-card p-5 sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-3xl">Pacjenci — szybki dostęp</h2>
              <p className="mt-2 text-xs text-muted-foreground">
                Pierwsze pozycje z listy aktywnych pacjentów.
              </p>
            </div>
            <Button asChild variant="outline">
              <Link href="/patients">Wszyscy pacjenci</Link>
            </Button>
          </div>

          <div className="mt-5">
            {active.isPending ? (
              <p role="status">Wczytywanie pacjentów…</p>
            ) : active.isError ? (
              <div className="space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {getApiMessage(active.error)}
                </p>
                <Button type="button" variant="outline" onClick={() => void active.refetch()}>
                  Spróbuj ponownie
                </Button>
              </div>
            ) : active.data.items.length === 0 ? (
              <div className="rounded-xl border border-dashed p-6 text-center">
                <h3 className="text-2xl">Brak aktywnych pacjentów</h3>
                <p className="mt-3 text-sm text-muted-foreground">
                  Utwórz kartę lub zaakceptuj prośbę pacjenta.
                </p>
              </div>
            ) : (
              <div className="divide-y rounded-xl border">
                {active.data.items.map((patient) => (
                  <PatientRow key={patient.id} patient={patient} />
                ))}
              </div>
            )}
          </div>
        </section>

        <aside className="space-y-6">
          <section className="space-y-4 rounded-2xl border bg-card p-5">
            <h2 className="text-3xl">Szybkie akcje</h2>
            {[
              ['/calendar', 'Kalendarz wizyt', 'Dodawanie i obsługa wizyt'],
              ['/settings/calendar', 'Dostępność', 'Godziny pracy, wyjątki i bufor'],
              ['/cooperation-requests', 'Prośby pacjentów', 'Rozpatrywanie współpracy'],
              ['/patients', 'Pacjenci', 'Wyszukiwanie i karty pacjentów'],
              ['/settings', 'Profil zawodowy', 'Dane widoczne w katalogu'],
            ].map(([href, label, description]) => (
              <Link
                key={href}
                href={href}
                className="block rounded-xl border p-4 transition-colors hover:bg-muted/40"
              >
                <span className="block text-sm font-medium">{label}</span>
                <span className="mt-2 block text-xs text-muted-foreground">
                  {description}
                </span>
              </Link>
            ))}
          </section>

          <section className="rounded-2xl border bg-primary/5 p-5">
            <h2 className="text-2xl">Planowanie wizyt</h2>
            <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
              Wolne terminy wynikają z Twoich godzin pracy, wyjątków,
              długości wizyt i bufora. Zmiana dostępności nie przesuwa
              wcześniej umówionych wizyt.
            </p>
          </section>
        </aside>
      </div>
    </div>
  );
}

function Stat({
  label,
  value,
  description,
}: {
  label: string;
  value: string;
  description: string;
}) {
  return (
    <article className="rounded-2xl border bg-card p-5">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="mt-3 break-words text-xl font-semibold">{value}</p>
      <p className="mt-2 text-xs text-muted-foreground">{description}</p>
    </article>
  );
}

function PatientRow({ patient }: { patient: PatientListItem }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 p-4">
      <div className="min-w-0">
        <p className="text-sm font-medium">
          {patient.lastName} {patient.firstName}
        </p>
        <p className="mt-2 break-words text-xs text-muted-foreground">
          {patient.email || patient.phoneNumber || 'Brak danych kontaktowych'}
        </p>
      </div>
      <Button asChild variant="outline" size="sm">
        <Link href={`/patients/${patient.id}`}>Otwórz kartę</Link>
      </Button>
    </div>
  );
}