'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { ArrowUpRight, CalendarClock, Settings2, UserPlus, Users } from 'lucide-react';

import TherapistUpcomingAppointments from '@/components/calendar/TherapistUpcomingAppointments';
import { Button } from '@/components/ui/button';
import { PracticeAnalytics } from '@/components/analytics/AnalyticsPanels';
import { patientsApi } from '@/lib/api';
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

  return (
    <div className="mx-auto max-w-7xl space-y-6 p-4 sm:p-6 lg:p-8">
      <header className="flex flex-wrap items-end justify-between gap-5 rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-5 sm:p-7">
        <div>
          <p className="text-xs font-semibold uppercase tracking-widest text-primary">Panel terapeuty</p>
          <h1 className="mt-2 text-4xl sm:text-5xl">Mój gabinet</h1>
          <p className="mt-3 text-sm text-muted-foreground">
            Sprawdź, kogo wkrótce przyjmujesz, i przejdź do kart pacjentów.
          </p>
        </div>

        <div className="flex flex-wrap gap-3">
          <Button asChild>
            <Link href="/patients/new">
              <UserPlus className="mr-2 h-4 w-4" aria-hidden="true" />
              Dodaj pacjenta
            </Link>
          </Button>
        </div>
      </header>

      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(240px,300px)]">
        <TherapistUpcomingAppointments accountId={accountId} />
        <section aria-labelledby="practice-shortcuts" className="space-y-3 rounded-2xl border bg-card p-5">
          <h2 id="practice-shortcuts" className="text-2xl">
            Skróty
          </h2>
          {[
            { href: '/cooperation-requests', label: 'Prośby o współpracę', icon: Users },
            { href: '/settings/calendar', label: 'Godziny pracy', icon: CalendarClock },
            { href: '/settings', label: 'Profil zawodowy', icon: Settings2 },
          ].map(({ href, label, icon: Icon }) => (
            <Link
              key={href}
              href={href}
              className="flex min-h-11 items-center gap-3 rounded-lg border px-3 py-2 text-sm font-medium transition-colors hover:bg-muted/50"
            >
              <Icon className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
              {label}
              <ArrowUpRight className="ml-auto h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            </Link>
          ))}
        </section>
      </div>

      <section aria-labelledby="practice-patients" className="rounded-2xl border bg-card p-5 sm:p-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 id="practice-patients" className="text-3xl">
              Pacjenci
            </h2>
            <p className="mt-2 text-xs text-muted-foreground">
              Szybki dostęp do aktywnych kart.
              {archived.data && !archived.isError && <> W archiwum: {archived.data.totalCount}.</>}
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

      <PracticeAnalytics />
    </div>
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
