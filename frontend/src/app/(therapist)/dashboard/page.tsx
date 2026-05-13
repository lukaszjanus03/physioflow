'use client';

import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import {
  Activity,
  Archive,
  ArrowRight,
  Calendar,
  ClipboardList,
  FileText,
  HeartPulse,
  Plus,
  Search,
  Settings,
  UserRound,
  Users,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import type { PatientListItem } from '@/types/api';

export default function DashboardPage() {
  const {
    data: activePatients,
    isLoading: activePatientsLoading,
    isError: activePatientsError,
  } = useQuery({
    queryKey: ['dashboard', 'patients', 'active'],
    queryFn: () =>
      patientsApi.list({
        pageNumber: 1,
        pageSize: 5,
      }),
  });

  const {
    data: archivedPatients,
    isLoading: archivedPatientsLoading,
  } = useQuery({
    queryKey: ['dashboard', 'patients', 'archived'],
    queryFn: () =>
      patientsApi.list({
        pageNumber: 1,
        pageSize: 1,
        onlyArchived: true,
      }),
  });

  const latestPatients = activePatients?.items ?? [];
  const activeCount = activePatients?.totalCount ?? 0;
  const archivedCount = archivedPatients?.totalCount ?? 0;
  const isLoading = activePatientsLoading || archivedPatientsLoading;

  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-6 p-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="inline-flex items-center gap-2 rounded-full border bg-background px-3 py-1 text-sm text-muted-foreground">
            <Activity className="h-4 w-4" />
            Panel terapeuty
          </div>

          <h1 className="mt-3 text-3xl font-semibold tracking-tight">Dashboard</h1>

          <p className="mt-1 max-w-2xl text-muted-foreground">
            Szybki dostęp do pacjentów, modułów i najważniejszych akcji w aktualnej wersji systemu.
          </p>
        </div>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild variant="outline">
            <Link href="/patients">
              <Search className="mr-2 h-4 w-4" />
              Przejdź do pacjentów
            </Link>
          </Button>

          <Button asChild>
            <Link href="/patients/new">
              <Plus className="mr-2 h-4 w-4" />
              Dodaj pacjenta
            </Link>
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          icon={<Users className="h-5 w-5" />}
          label="Aktywni pacjenci"
          value={isLoading ? '...' : String(activeCount)}
          description="Pacjenci widoczni na głównej liście"
        />

        <StatCard
          icon={<Archive className="h-5 w-5" />}
          label="Archiwalni"
          value={isLoading ? '...' : String(archivedCount)}
          description="Pacjenci przeniesieni do archiwum"
        />

        <StatCard
          icon={<HeartPulse className="h-5 w-5" />}
          label="Body Mapping"
          value="Aktywny"
          description="Moduł map bólu jest dostępny"
        />

        <StatCard
          icon={<Calendar className="h-5 w-5" />}
          label="Wizyty"
          value="Wkrótce"
          description="Brak danych bez modułu kalendarza"
          muted
        />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
        <Card>
          <CardHeader className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle>Ostatni aktywni pacjenci</CardTitle>
              <CardDescription>
                Najnowsze rekordy z aktualnej listy pacjentów.
              </CardDescription>
            </div>

            <Button asChild variant="outline" size="sm">
              <Link href="/patients">
                Zobacz wszystkich
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          </CardHeader>

          <CardContent>
            {activePatientsLoading && <PatientListSkeleton />}

            {activePatientsError && (
              <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-4 text-sm text-destructive">
                Nie udało się pobrać pacjentów. Sprawdź, czy backend działa poprawnie.
              </div>
            )}

            {!activePatientsLoading && !activePatientsError && latestPatients.length === 0 && (
              <EmptyPatientsState />
            )}

            {!activePatientsLoading && !activePatientsError && latestPatients.length > 0 && (
              <div className="divide-y rounded-lg border">
                {latestPatients.map((patient) => (
                  <PatientRow key={patient.id} patient={patient} />
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Szybkie akcje</CardTitle>
              <CardDescription>
                Najczęstsze operacje w aktualnym zakresie projektu.
              </CardDescription>
            </CardHeader>

            <CardContent className="space-y-3">
              <QuickAction
                href="/patients/new"
                icon={<Plus className="h-4 w-4" />}
                label="Dodaj pacjenta"
                description="Utwórz nową kartę pacjenta"
              />

              <QuickAction
                href="/patients"
                icon={<Users className="h-4 w-4" />}
                label="Lista pacjentów"
                description="Szukaj, filtruj i otwieraj karty"
              />

              <QuickAction
                href="/calendar"
                icon={<Calendar className="h-4 w-4" />}
                label="Kalendarz"
                description="Moduł wizyt w przygotowaniu"
              />

              <QuickAction
                href="/settings"
                icon={<Settings className="h-4 w-4" />}
                label="Ustawienia"
                description="Konfiguracja konta i aplikacji"
              />
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Stan modułów</CardTitle>
              <CardDescription>
                Co jest dostępne w tej wersji frontendu.
              </CardDescription>
            </CardHeader>

            <CardContent className="space-y-3">
              <ModuleStatus
                icon={<Users className="h-4 w-4" />}
                label="Pacjenci"
                status="Gotowe"
                active
              />

              <ModuleStatus
                icon={<HeartPulse className="h-4 w-4" />}
                label="Body Mapping"
                status="Gotowe frontendowo"
                active
              />

              <ModuleStatus
                icon={<Calendar className="h-4 w-4" />}
                label="Wizyty"
                status="W przygotowaniu"
              />

              <ModuleStatus
                icon={<FileText className="h-4 w-4" />}
                label="EDM / dokumenty"
                status="W przygotowaniu"
              />

              <ModuleStatus
                icon={<ClipboardList className="h-4 w-4" />}
                label="Plany terapii"
                status="W przygotowaniu"
              />
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}

function StatCard({
  icon,
  label,
  value,
  description,
  muted = false,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  description: string;
  muted?: boolean;
}) {
  return (
    <Card className={muted ? 'opacity-75' : undefined}>
      <CardContent className="p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-sm text-muted-foreground">{label}</p>
            <p className="mt-2 text-2xl font-semibold">{value}</p>
            <p className="mt-1 text-xs text-muted-foreground">{description}</p>
          </div>

          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            {icon}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function PatientRow({ patient }: { patient: PatientListItem }) {
  return (
    <div className="flex flex-col gap-3 p-4 transition hover:bg-muted/40 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex min-w-0 items-center gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 font-semibold text-primary">
          {getInitials(patient)}
        </div>

        <div className="min-w-0">
          <p className="truncate font-medium">
            {patient.lastName} {patient.firstName}
          </p>

          <p className="truncate text-sm text-muted-foreground">
            {patient.email || patient.phoneNumber || 'Brak danych kontaktowych'}
          </p>
        </div>
      </div>

      <Button asChild variant="outline" size="sm">
        <Link href={`/patients/${patient.id}`}>
          Otwórz
          <ArrowRight className="ml-2 h-4 w-4" />
        </Link>
      </Button>
    </div>
  );
}

function QuickAction({
  href,
  icon,
  label,
  description,
}: {
  href: string;
  icon: React.ReactNode;
  label: string;
  description: string;
}) {
  return (
    <Link
      href={href}
      className="flex items-center gap-3 rounded-lg border p-3 transition hover:bg-muted/50"
    >
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
        {icon}
      </div>

      <div className="min-w-0 flex-1">
        <p className="font-medium">{label}</p>
        <p className="truncate text-sm text-muted-foreground">{description}</p>
      </div>

      <ArrowRight className="h-4 w-4 text-muted-foreground" />
    </Link>
  );
}

function ModuleStatus({
  icon,
  label,
  status,
  active = false,
}: {
  icon: React.ReactNode;
  label: string;
  status: string;
  active?: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border p-3">
      <div className="flex min-w-0 items-center gap-3">
        <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
          {icon}
        </div>

        <p className="truncate font-medium">{label}</p>
      </div>

      <span
        className={`shrink-0 rounded-full px-2.5 py-1 text-xs font-medium ${
          active
            ? 'bg-emerald-50 text-emerald-700'
            : 'bg-muted text-muted-foreground'
        }`}
      >
        {status}
      </span>
    </div>
  );
}

function EmptyPatientsState() {
  return (
    <div className="flex flex-col items-center justify-center rounded-lg border border-dashed p-10 text-center">
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
        <UserRound className="h-6 w-6" />
      </div>

      <h2 className="mt-4 font-semibold">Brak pacjentów</h2>

      <p className="mt-1 max-w-sm text-sm text-muted-foreground">
        Dodaj pierwszego pacjenta, żeby dashboard zaczął pokazywać realne dane.
      </p>

      <Button asChild className="mt-5">
        <Link href="/patients/new">
          <Plus className="mr-2 h-4 w-4" />
          Dodaj pacjenta
        </Link>
      </Button>
    </div>
  );
}

function PatientListSkeleton() {
  return (
    <div className="space-y-3">
      {Array.from({ length: 4 }).map((_, index) => (
        <div key={index} className="flex items-center gap-3 rounded-lg border p-4">
          <div className="h-10 w-10 animate-pulse rounded-lg bg-muted" />
          <div className="flex-1 space-y-2">
            <div className="h-4 w-40 animate-pulse rounded bg-muted" />
            <div className="h-3 w-56 animate-pulse rounded bg-muted" />
          </div>
        </div>
      ))}
    </div>
  );
}

function getInitials(patient: PatientListItem) {
  const firstInitial = patient.firstName?.trim().charAt(0) ?? '';
  const lastInitial = patient.lastName?.trim().charAt(0) ?? '';
  const initials = `${firstInitial}${lastInitial}`.toUpperCase();

  return initials || 'P';
}
