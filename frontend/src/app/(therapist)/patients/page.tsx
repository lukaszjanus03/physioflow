'use client';

import Link from 'next/link';
import type { ChangeEvent } from 'react';
import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Archive,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  Mail,
  Phone,
  Plus,
  Search,
  UserRound,
  Users,
  X,
} from 'lucide-react';

import { patientsApi } from '@/lib/api';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import type { PatientListItem } from '@/types/api';

type FilterMode = 'active' | 'all' | 'archived';

const PAGE_SIZE = 20;

const FILTERS: Array<{ value: FilterMode; label: string; description: string }> = [
  { value: 'active', label: 'Aktywni', description: 'Pacjenci aktualnie prowadzeni' },
  { value: 'all', label: 'Wszyscy', description: 'Aktywni i archiwalni pacjenci' },
  { value: 'archived', label: 'Archiwalni', description: 'Pacjenci przeniesieni do archiwum' },
];

export default function PatientsPage() {
  const [pageNumber, setPageNumber] = useState(1);
  const [searchTerm, setSearchTerm] = useState('');
  const [filterMode, setFilterMode] = useState<FilterMode>('active');

  const { data, isLoading, isError } = useQuery({
    queryKey: ['patients', pageNumber, searchTerm, filterMode],
    queryFn: () =>
      patientsApi.list({
        pageNumber,
        pageSize: PAGE_SIZE,
        search: searchTerm.trim() || undefined,
        includeArchived: filterMode === 'all' ? true : undefined,
        onlyArchived: filterMode === 'archived' ? true : undefined,
      }),
  });

  const patients = (data?.items ?? []) as PatientListItem[];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 1;

  const activeFilter = useMemo(
    () => FILTERS.find((filter) => filter.value === filterMode) ?? FILTERS[0],
    [filterMode]
  );

  const hasSearch = searchTerm.trim().length > 0;
  const hasPatients = patients.length > 0;

  const firstVisiblePatient = totalCount === 0 ? 0 : (pageNumber - 1) * PAGE_SIZE + 1;
  const lastVisiblePatient = Math.min(pageNumber * PAGE_SIZE, totalCount);

  function handleSearchChange(value: string) {
    setSearchTerm(value);
    setPageNumber(1);
  }

  function handleFilterChange(value: FilterMode) {
    setFilterMode(value);
    setPageNumber(1);
  }

  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-6 p-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <div className="inline-flex items-center gap-2 rounded-full border bg-background px-3 py-1 text-sm text-muted-foreground">
            <Users className="h-4 w-4" />
            Lista pacjentów
          </div>

          <h1 className="mt-3 text-3xl font-semibold tracking-tight">Pacjenci</h1>

          <p className="mt-1 max-w-2xl text-muted-foreground">
            Zarządzaj kartami pacjentów, szybko wyszukuj kontakt i przechodź do szczegółów leczenia.
          </p>
        </div>

        <Button asChild className="w-full gap-2 lg:w-auto">
          <Link href="/patients/new">
            <Plus className="h-4 w-4" />
            Dodaj pacjenta
          </Link>
        </Button>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <InfoTile
          label="Widok"
          value={activeFilter.label}
          description={activeFilter.description}
        />

        <InfoTile
          label="Wyniki"
          value={String(totalCount)}
          description={getPatientsCountLabel(totalCount)}
        />

        <InfoTile
          label="Strona"
          value={`${pageNumber} / ${Math.max(totalPages, 1)}`}
          description={totalCount > 0 ? `${firstVisiblePatient}-${lastVisiblePatient} z ${totalCount}` : 'Brak rekordów'}
        />
      </div>

      <div className="rounded-lg border bg-card p-4 text-card-foreground shadow-sm">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <div className="relative w-full xl:max-w-xl">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />

            <input
              value={searchTerm}
              onChange={(event: ChangeEvent<HTMLInputElement>) => handleSearchChange(event.target.value)}
              placeholder="Szukaj po imieniu, nazwisku, e-mailu..."
              className="flex h-11 w-full rounded-md border border-input bg-background px-3 py-2 pl-10 pr-10 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
            />

            {hasSearch && (
              <button
                type="button"
                onClick={() => handleSearchChange('')}
                className="absolute right-2 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition hover:bg-muted hover:text-foreground"
                aria-label="Wyczyść wyszukiwanie"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>

          <div className="grid grid-cols-3 rounded-lg bg-muted p-1">
            {FILTERS.map((filter) => (
              <button
                key={filter.value}
                type="button"
                onClick={() => handleFilterChange(filter.value)}
                className={cn(
                  'rounded-md px-4 py-2 text-sm font-medium transition',
                  filterMode === filter.value
                    ? 'bg-background text-foreground shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
                )}
              >
                {filter.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      {isLoading && <PatientListSkeleton />}

      {isError && (
        <div className="flex flex-col items-center justify-center rounded-lg border bg-card p-10 text-center text-card-foreground shadow-sm">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-destructive/10 text-destructive">
            <Archive className="h-6 w-6" />
          </div>
          <h2 className="mt-4 text-lg font-semibold">Nie udało się pobrać pacjentów</h2>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            Sprawdź, czy backend działa poprawnie i odśwież stronę.
          </p>
        </div>
      )}

      {!isLoading && !isError && !hasPatients && (
        <div className="flex flex-col items-center justify-center rounded-lg border bg-card p-12 text-center text-card-foreground shadow-sm">
          <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Users className="h-7 w-7" />
          </div>

          <h2 className="mt-4 text-lg font-semibold">
            {hasSearch ? 'Brak pacjentów dla podanego wyszukiwania' : 'Brak pacjentów w tym widoku'}
          </h2>

          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            {hasSearch
              ? 'Spróbuj wpisać inne imię, nazwisko albo adres e-mail.'
              : 'Dodaj pierwszego pacjenta, aby rozpocząć prowadzenie dokumentacji.'}
          </p>

          {!hasSearch && (
            <Button asChild className="mt-5 gap-2">
              <Link href="/patients/new">
                <Plus className="h-4 w-4" />
                Dodaj pacjenta
              </Link>
            </Button>
          )}
        </div>
      )}

      {!isLoading && !isError && hasPatients && (
        <div className="overflow-hidden rounded-lg border bg-card text-card-foreground shadow-sm">
          <div className="divide-y">
            {patients.map((patient: PatientListItem) => (
              <PatientRow key={patient.id} patient={patient} />
            ))}
          </div>
        </div>
      )}

      {!isLoading && !isError && totalPages > 1 && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-sm text-muted-foreground">
            Pokazujesz {firstVisiblePatient}-{lastVisiblePatient} z {totalCount} pacjentów
          </p>

          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              disabled={pageNumber <= 1}
              onClick={() => setPageNumber((page) => Math.max(page - 1, 1))}
              className="gap-2"
            >
              <ChevronLeft className="h-4 w-4" />
              Poprzednia
            </Button>

            <Button
              variant="outline"
              disabled={pageNumber >= totalPages}
              onClick={() => setPageNumber((page) => Math.min(page + 1, totalPages))}
              className="gap-2"
            >
              Następna
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

function PatientRow({ patient }: { patient: PatientListItem }) {
  const fullName = `${patient.lastName} ${patient.firstName}`.trim();
  const email = patient.email?.trim();
  const phoneNumber = patient.phoneNumber?.trim();

  return (
    <div className="flex flex-col gap-4 p-4 transition hover:bg-muted/40 lg:flex-row lg:items-center lg:justify-between">
      <div className="flex min-w-0 gap-4">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-primary/10 font-semibold text-primary">
          {getInitials(patient)}
        </div>

        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="truncate text-base font-semibold">{fullName || 'Pacjent bez nazwy'}</h2>

            <span
              className={cn(
                'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium',
                patient.isArchived
                  ? 'bg-muted text-muted-foreground'
                  : 'bg-emerald-50 text-emerald-700'
              )}
            >
              {patient.isArchived ? 'Archiwalny' : 'Aktywny'}
            </span>
          </div>

          <div className="mt-2 flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:flex-wrap sm:items-center">
            <span className="inline-flex items-center gap-1.5">
              <Mail className="h-4 w-4" />
              {email || 'Brak e-maila'}
            </span>

            <span className="inline-flex items-center gap-1.5">
              <Phone className="h-4 w-4" />
              {phoneNumber || 'Brak telefonu'}
            </span>

            <span className="inline-flex items-center gap-1.5">
              <CalendarDays className="h-4 w-4" />
              Dodano {formatDate(patient.createdAt)}
            </span>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2 lg:shrink-0">
        <Button asChild variant="outline" className="w-full gap-2 lg:w-auto">
          <Link href={`/patients/${patient.id}`}>
            <UserRound className="h-4 w-4" />
            Otwórz kartę
          </Link>
        </Button>
      </div>
    </div>
  );
}

function InfoTile({
  label,
  value,
  description,
}: {
  label: string;
  value: string;
  description: string;
}) {
  return (
    <div className="rounded-lg border bg-card p-4 text-card-foreground shadow-sm">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
      <p className="mt-1 text-sm text-muted-foreground">{description}</p>
    </div>
  );
}

function PatientListSkeleton() {
  return (
    <div className="overflow-hidden rounded-lg border bg-card text-card-foreground shadow-sm">
      <div className="divide-y">
        {Array.from({ length: 5 }).map((_, index) => (
          <div key={index} className="flex items-center gap-4 p-4">
            <div className="h-12 w-12 animate-pulse rounded-xl bg-muted" />

            <div className="flex-1 space-y-3">
              <div className="h-4 w-48 animate-pulse rounded bg-muted" />
              <div className="h-3 w-full max-w-xl animate-pulse rounded bg-muted" />
            </div>

            <div className="hidden h-10 w-28 animate-pulse rounded-md bg-muted lg:block" />
          </div>
        ))}
      </div>
    </div>
  );
}

function getInitials(patient: PatientListItem) {
  const firstInitial = patient.firstName?.trim().charAt(0) ?? '';
  const lastInitial = patient.lastName?.trim().charAt(0) ?? '';
  const initials = `${firstInitial}${lastInitial}`.toUpperCase();

  return initials || 'P';
}

function getPatientsCountLabel(count: number) {
  if (count === 1) return 'pacjent';
  return 'pacjentów';
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('pl-PL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(value));
}
