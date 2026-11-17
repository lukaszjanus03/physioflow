'use client';

import { useCallback, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { BookCheck, CalendarDays, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import { Pages } from '@/components/therapy/TherapyUI';
import { usePatientContent } from '@/hooks/use-patient-content';
import {
  journalApi,
  journalError,
  journalStatuses,
  type JournalEntry,
  type JournalFilters,
} from '@/lib/exercise-journal-api';
import { journalHref, validateJournalFilters } from '@/lib/exercise-journal-form';
import { JournalEntryView } from './JournalEntryView';
import { JournalEditor } from './JournalEditor';
import { JournalDeleteDialog } from './JournalDeleteDialog';
import { useJournalNavigation } from './JournalNavigation';

export default function PatientJournal({ accountId }: { accountId: string }) {
  const params = useSearchParams();
  const key = params.toString();
  return <JournalPage key={`${accountId}:${key}`} query={key} />;
}

function JournalPage({ query }: { query: string }) {
  const params = new URLSearchParams(query);
  const applied: JournalFilters = {
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    status: (params.get('status') ?? '') as JournalFilters['status'],
    therapyPlanId: params.get('therapyPlanId') ?? '',
  };
  const rawPage = Number(params.get('page') ?? 1);
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 && rawPage <= 214748364 ? rawPage : 1;
  const router = useRouter();
  const { confirmLeave } = useJournalNavigation();
  const [filters, setFilters] = useState(applied);
  const [filterError, setFilterError] = useState<string | null>(null);
  const appliedError = validateJournalFilters(applied);
  const navigate = (next: JournalFilters, nextPage = 1) =>
    confirmLeave(() => router.push(journalHref(next, nextPage), { scroll: true }));

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header className="relative overflow-hidden rounded-2xl border bg-card p-6 shadow-sm sm:p-8">
        <p className="mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-primary">
          <BookCheck size={18} aria-hidden="true" />
          Twoja aktywność
        </p>
        <h1 className="text-4xl">Mój dziennik</h1>
        <p className="mt-3 max-w-2xl text-sm leading-7 text-muted-foreground">
          Twoje zgłoszenia wykonania ćwiczeń. Brak wpisu nie oznacza pominięcia. Nowe wykonanie zapiszesz przy
          ćwiczeniu w udostępnionym planie.
        </p>
        <p className="mt-2 text-xs leading-6 text-muted-foreground">
          Historia należy do Twojego konta. Możesz edytować i usuwać własne wpisy również po cofnięciu
          udostępnienia planu.
        </p>
      </header>
      <form
        noValidate
        className="space-y-4 rounded-2xl border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          const error = validateJournalFilters(filters);
          setFilterError(error);
          if (!error) navigate(filters);
        }}
      >
        <div className="grid items-end gap-4 sm:grid-cols-2 lg:grid-cols-[1fr_1fr_1.3fr_auto]">
          <label className="space-y-2 text-sm">
            <span>Od dnia</span>
            <Input
              type="date"
              min="1900-01-01"
              value={filters.from}
              onChange={(e) => setFilters((old) => ({ ...old, from: e.target.value }))}
            />
          </label>
          <label className="space-y-2 text-sm">
            <span>Do dnia</span>
            <Input
              type="date"
              min="1900-01-01"
              value={filters.to}
              onChange={(e) => setFilters((old) => ({ ...old, to: e.target.value }))}
            />
          </label>
          <label className="space-y-2 text-sm">
            <span>Status realizacji</span>
            <select
              className="h-10 w-full rounded-lg border bg-background px-3"
              value={filters.status}
              onChange={(e) =>
                setFilters((old) => ({ ...old, status: e.target.value as JournalFilters['status'] }))
              }
            >
              <option value="">Wszystkie statusy</option>
              {Object.entries(journalStatuses).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <div className="flex gap-2">
            <Button type="submit">Zastosuj</Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => navigate({ from: '', to: '', status: '', therapyPlanId: '' })}
            >
              Wyczyść
            </Button>
          </div>
        </div>
        {applied.therapyPlanId && (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-primary/5 p-3 text-sm">
            <p>Historia wybranego planu — wszystkie zapisane wersje.</p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => navigate({ ...applied, therapyPlanId: '' })}
            >
              Pokaż wszystkie plany
            </Button>
          </div>
        )}
        {(filterError || appliedError) && (
          <p role="alert" className="text-sm text-destructive">
            {filterError || appliedError}
          </p>
        )}
      </form>
      {!appliedError && <JournalResults filters={applied} page={page} navigate={navigate} />}
    </div>
  );
}

function JournalResults({
  filters,
  page,
  navigate,
}: {
  filters: JournalFilters;
  page: number;
  navigate: (filters: JournalFilters, page?: number) => void;
}) {
  const { from, to, therapyPlanId, status } = filters;
  const load = useCallback(
    (signal: AbortSignal) => journalApi.list({ from, to, therapyPlanId, status }, page, signal),
    [from, to, therapyPlanId, status, page],
  );
  const [editing, setEditing] = useState<JournalEntry | null>(null);
  const [deleting, setDeleting] = useState<JournalEntry | null>(null);
  const query = usePatientContent(load, Boolean(editing || deleting));
  const hasFilters = Boolean(from || to || therapyPlanId || status);
  // Background refreshes must not replace or discard an open patient form.
  const reload = () => {
    setEditing(null);
    setDeleting(null);
    query.reload();
  };

  if (query.loading) return <LoadingState title="Pobieranie dziennika…" />;
  if (query.error)
    return (
      <section className="space-y-4 rounded-2xl border bg-card p-6">
        <p role="alert" className="text-sm text-destructive">
          {journalError(query.error)}
        </p>
        <Button variant="outline" onClick={query.reload}>
          Pobierz dziennik ponownie
        </Button>
      </section>
    );
  if (!query.data) return null;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">Wpisy: {query.data.totalCount}</p>
        <Button
          type="button"
          variant="outline"
          disabled={Boolean(editing || deleting)}
          onClick={query.reload}
        >
          Odśwież dziennik
        </Button>
      </div>
      {!query.data.items.length && (
        <section className="space-y-3 rounded-2xl border bg-card p-8 text-center">
          <CalendarDays className="mx-auto text-primary" aria-hidden="true" />
          <h2 className="text-3xl">
            {page > 1
              ? 'Brak wpisów na tej stronie'
              : hasFilters
                ? 'Brak wpisów dla wybranych filtrów'
                : 'Twój dziennik czeka na pierwszy wpis'}
          </h2>
          <p className="text-sm leading-7 text-muted-foreground">
            {page > 1
              ? 'Wróć do poprzedniej strony lub zmień filtry.'
              : 'Otwórz udostępniony plan i wybierz „Zapisz wykonanie” przy ćwiczeniu. Nie oznaczamy automatycznie dni bez zgłoszeń jako pominiętych.'}
          </p>
        </section>
      )}
      <div className="grid min-w-0 gap-4 lg:grid-cols-2">
        {query.data.items.map((entry) => (
          <article
            key={entry.id}
            className="flex min-w-0 flex-col overflow-hidden rounded-2xl border bg-card shadow-sm"
          >
            <header className="space-y-2 border-b bg-muted/15 p-5">
              <p className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
                {entry.planName || 'Plan terapii'} · wersja {entry.planVersionNumber}
              </p>
              <h2 className="text-2xl [overflow-wrap:anywhere]">{entry.exercise.name}</h2>
            </header>
            <div className="flex-1 p-5">
              <JournalEntryView entry={entry} />
            </div>
            <footer className="flex flex-wrap gap-2 border-t p-4">
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="gap-2"
                onClick={() => setEditing(entry)}
              >
                <Pencil size={14} aria-hidden="true" />
                Edytuj wpis
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="gap-2 text-destructive"
                onClick={() => setDeleting(entry)}
              >
                <Trash2 size={14} aria-hidden="true" />
                Usuń wpis
              </Button>
              {!therapyPlanId && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => navigate({ ...filters, therapyPlanId: entry.therapyPlanId })}
                >
                  Historia tego planu
                </Button>
              )}
            </footer>
          </article>
        ))}
      </div>
      {(query.data.totalPages > 1 || page > 1) && (
        <Pages
          page={page}
          pages={query.data.totalPages}
          next={query.data.hasNext}
          busy={Boolean(editing || deleting)}
          onChange={(next) => navigate(filters, next)}
        />
      )}
      {editing && (
        <JournalEditor
          key={editing.id}
          source={{ entry: editing }}
          onClose={() => setEditing(null)}
          onSaved={reload}
        />
      )}
      {deleting && (
        <JournalDeleteDialog
          key={deleting.id}
          entry={deleting}
          onClose={() => setDeleting(null)}
          onDeleted={reload}
        />
      )}
    </div>
  );
}
