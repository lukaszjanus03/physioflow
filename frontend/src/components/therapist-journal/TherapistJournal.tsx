'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  ArrowLeft,
  BookCheck,
  CheckCircle2,
  CircleDashed,
  CalendarDays,
  MinusCircle,
  RefreshCw,
  ShieldCheck,
  X,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Pages } from '@/components/therapy/TherapyUI';
import {
  JournalDose,
  JournalReportView,
  JournalStatusBadge,
} from '@/components/patient-journal/JournalEntryView';
import { usePatientContent } from '@/hooks/use-patient-content';
import { useAuthStore } from '@/stores/auth-store';
import { accessDenied } from '@/lib/patient-sharing-api';
import { journalStatuses, type JournalEntry } from '@/lib/exercise-journal-api';
import { recordDate } from '@/lib/medical-records-api';
import {
  journalCalendarDate,
  journalUuid,
  therapistJournalApi,
  therapistJournalError,
  therapistJournalFilterError,
  therapistJournalHref,
  therapistJournalOverview,
  type JournalSummary,
  type TherapistJournalFilters,
} from '@/lib/therapist-journal-api';

export default function TherapistJournal({ patientId }: { patientId: string }) {
  const { userId, roles, isAuthenticated } = useAuthStore();
  if (!journalUuid(patientId))
    return (
      <p role="alert" className="p-6">
        Nieprawidłowy adres pacjenta.
      </p>
    );
  if (!userId || !isAuthenticated || !roles.includes('Therapist'))
    return (
      <p role="alert" className="p-6">
        Podgląd wymaga zalogowanego konta terapeuty.
      </p>
    );
  return <JournalScope key={`${userId}:${patientId}`} patientId={patientId} />;
}

function JournalScope({ patientId }: { patientId: string }) {
  const params = useSearchParams();
  const [denied, setDenied] = useState<{ error: unknown } | null>(null);
  const onDenied = useCallback((error: unknown) => setDenied({ error }), []);
  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      <nav aria-label="Kontekst dziennika" className="flex flex-wrap gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link href={`/patients/${patientId}`}>
            <ArrowLeft size={15} className="mr-2" aria-hidden="true" />
            Karta pacjenta
          </Link>
        </Button>
        <Button asChild variant="outline" size="sm">
          <Link href={`/patients/${patientId}/therapy-plans`}>Plany terapii</Link>
        </Button>
      </nav>
      <header className="relative overflow-hidden rounded-2xl border bg-card p-6 shadow-sm sm:p-8">
        <p className="mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-primary">
          <BookCheck size={18} aria-hidden="true" />
          Pomiędzy wizytami
        </p>
        <h1 className="text-4xl">Dziennik ćwiczeń pacjenta</h1>
        <p className="mt-3 max-w-3xl text-sm leading-7 text-muted-foreground">
          Zgłoszenia pacjenta z zapisanych wersji planów. Dane opisują deklarowane wykonanie, nie ocenę
          skuteczności terapii.
        </p>
        <p className="mt-4 flex items-center gap-2 text-xs text-muted-foreground">
          <ShieldCheck size={15} aria-hidden="true" />
          Tylko odczyt · dostęp sprawdzany przez serwer
        </p>
      </header>
      {denied ? (
        <section className="space-y-4 rounded-2xl border bg-card p-6">
          <p role="alert" className="text-sm leading-7 text-destructive">
            {therapistJournalError(denied.error)}
          </p>
          <p className="text-sm text-muted-foreground">
            Wcześniej pobrane wpisy i podsumowanie zostały usunięte z podglądu. Zmiana filtrów nie przywraca
            dostępu.
          </p>
          <Button type="button" variant="outline" onClick={() => setDenied(null)}>
            Sprawdź dostęp i pobierz od nowa
          </Button>
        </section>
      ) : (
        <JournalWorkspace
          key={params.toString()}
          patientId={patientId}
          query={params.toString()}
          onDenied={onDenied}
        />
      )}
    </div>
  );
}

function JournalWorkspace({
  patientId,
  query,
  onDenied,
}: {
  patientId: string;
  query: string;
  onDenied: (error: unknown) => void;
}) {
  const router = useRouter();
  const params = new URLSearchParams(query);
  const applied: TherapistJournalFilters = {
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    therapyPlanId: params.get('therapyPlanId') ?? '',
    planItemId: params.get('planItemId') ?? '',
    status: (params.get('status') ?? '') as TherapistJournalFilters['status'],
  };
  const number = Number(params.get('page') ?? 1);
  const page = Number.isSafeInteger(number) && number >= 1 && number <= 214748364 ? number : 1;
  const [draft, setDraft] = useState(applied);
  const [error, setError] = useState<string | null>(null);
  const appliedError = therapistJournalFilterError(applied);
  function navigate(filters: TherapistJournalFilters, nextPage = 1) {
    router.push(therapistJournalHref(patientId, filters, nextPage), { scroll: true });
  }
  return (
    <>
      <form
        noValidate
        className="space-y-4 rounded-2xl border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          const problem = therapistJournalFilterError(draft);
          setError(problem);
          if (!problem) navigate(draft);
        }}
      >
        <div className="grid items-end gap-4 sm:grid-cols-2 lg:grid-cols-[1fr_1fr_1.2fr_auto]">
          <label className="space-y-2 text-sm">
            <span>Od dnia wykonania</span>
            <Input
              type="date"
              min="1900-01-01"
              value={draft.from}
              onChange={(e) => setDraft((old) => ({ ...old, from: e.target.value }))}
            />
          </label>
          <label className="space-y-2 text-sm">
            <span>Do dnia wykonania</span>
            <Input
              type="date"
              min="1900-01-01"
              value={draft.to}
              onChange={(e) => setDraft((old) => ({ ...old, to: e.target.value }))}
            />
          </label>
          <label className="space-y-2 text-sm">
            <span>Status realizacji</span>
            <select
              className="h-10 w-full rounded-lg border bg-background px-3"
              value={draft.status}
              onChange={(e) =>
                setDraft((old) => ({ ...old, status: e.target.value as TherapistJournalFilters['status'] }))
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
              onClick={() => navigate({ from: '', to: '', status: '', therapyPlanId: '', planItemId: '' })}
            >
              Wyczyść
            </Button>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {applied.therapyPlanId && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => navigate({ ...applied, therapyPlanId: '' })}
            >
              Wybrany plan · wszystkie wersje
              <X size={13} className="ml-2" aria-hidden="true" />
              <span className="sr-only">Usuń filtr planu</span>
            </Button>
          )}
          {applied.planItemId && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => navigate({ ...applied, planItemId: '' })}
            >
              Wybrana pozycja ćwiczenia
              <X size={13} className="ml-2" aria-hidden="true" />
              <span className="sr-only">Usuń filtr pozycji</span>
            </Button>
          )}
        </div>
        <p className="text-xs leading-6 text-muted-foreground">
          Daty wykonania według Europe/Warsaw. Historię planu lub konkretnej pozycji możesz wybrać przy
          wpisie. Pozycja nie oznacza wszystkich wystąpień tego ćwiczenia w bibliotece.
        </p>
        {(error || appliedError) && (
          <p role="alert" className="text-sm text-destructive">
            {error || appliedError}
          </p>
        )}
      </form>
      {!appliedError && (
        <JournalResults
          patientId={patientId}
          filters={applied}
          page={page}
          navigate={navigate}
          onDenied={onDenied}
        />
      )}
    </>
  );
}

function SummaryTiles({ summary }: { summary: JournalSummary }) {
  const tiles = [
    { label: 'Pełne wykonania', value: summary.completedCount, icon: CheckCircle2 },
    { label: 'Częściowe wykonania', value: summary.partialCount, icon: CircleDashed },
    { label: 'Zgłoszone pominięcia', value: summary.skippedCount, icon: MinusCircle },
    { label: 'Ostatnie wykonanie', value: journalCalendarDate(summary.lastPerformedOn), icon: CalendarDays },
  ];
  return (
    <section aria-label="Podsumowanie realizacji" className="space-y-3">
      <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tiles.map(({ label, value, icon: Icon }) => (
          <div key={label} className="min-w-0 rounded-2xl border bg-card p-4 sm:p-5">
            <dt className="flex items-start gap-2 text-xs leading-6 text-muted-foreground">
              <Icon size={16} className="mt-1 shrink-0 text-primary" aria-hidden="true" />
              {label}
            </dt>
            <dd className="mt-3 text-xl font-semibold [overflow-wrap:anywhere] sm:text-2xl">{value}</dd>
          </div>
        ))}
      </dl>
      <p className="text-xs leading-6 text-muted-foreground">
        Cały przefiltrowany zakres, nie tylko bieżąca strona. Brak wpisu nie oznacza pominięcia. Ostatnia data
        obejmuje tylko pełne i częściowe wykonania; osobne zgłoszenia tego samego dnia liczą się osobno.
      </p>
    </section>
  );
}

function JournalResults({
  patientId,
  filters,
  page,
  navigate,
  onDenied,
}: {
  patientId: string;
  filters: TherapistJournalFilters;
  page: number;
  navigate: (filters: TherapistJournalFilters, page?: number) => void;
  onDenied: (error: unknown) => void;
}) {
  const { from, to, status, therapyPlanId, planItemId } = filters;
  const [entryId, setEntryId] = useState<string | null>(null);
  const load = useCallback(
    async (signal: AbortSignal) => {
      try {
        return await therapistJournalOverview(
          patientId,
          { from, to, status, therapyPlanId, planItemId },
          page,
          signal,
        );
      } catch (error) {
        if (!signal.aborted && accessDenied(error)) onDenied(error);
        throw error;
      }
    },
    [patientId, from, to, status, therapyPlanId, planItemId, page, onDenied],
  );
  const query = usePatientContent(load);
  if (query.loading) return <LoadingState title="Pobieranie dziennika i podsumowania…" />;
  if (query.error) return <ReadFailure error={query.error} retry={query.reload} />;
  if (!query.data) return null;
  const { list, summary, coherent } = query.data;
  const hasFilters = Boolean(from || to || status || therapyPlanId || planItemId);
  return (
    <div className="space-y-5">
      {coherent ? (
        <SummaryTiles summary={summary} />
      ) : (
        <section role="status" className="space-y-3 rounded-xl border border-amber-200 bg-amber-50/50 p-4">
          <p className="text-sm">
            Pacjent zmienił wpisy podczas odczytu. Liczniki są chwilowo ukryte, aby nie przedstawiać
            niespójnego podsumowania.
          </p>
          <Button type="button" variant="outline" onClick={query.reload}>
            Odśwież listę i podsumowanie
          </Button>
        </section>
      )}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">Wpisy w zakresie: {list.totalCount}</p>
        <Button type="button" variant="outline" className="gap-2" onClick={query.reload}>
          <RefreshCw size={15} aria-hidden="true" />
          Odśwież dziennik
        </Button>
      </div>
      {!list.items.length && (
        <section className="space-y-3 rounded-2xl border bg-card p-8 text-center">
          <BookCheck className="mx-auto text-primary" aria-hidden="true" />
          <h2 className="text-3xl">
            {page > 1
              ? 'Brak wpisów na tej stronie'
              : hasFilters
                ? 'Brak wpisów dla wybranych filtrów'
                : 'Pacjent nie ma jeszcze zgłoszeń w dostępnej historii'}
          </h2>
          <p className="text-sm leading-7 text-muted-foreground">
            {page > 1
              ? 'Wróć do poprzedniej strony lub zmień filtry.'
              : 'Podgląd obejmuje wyłącznie historię dostępną w bieżącym okresie powiązania konta. Nie oznacza to niewykonywania ćwiczeń.'}
          </p>
        </section>
      )}
      <div className="space-y-6">
        {list.items.map((entry, index) => (
          <section key={entry.id} className="min-w-0">
            {(index === 0 || list.items[index - 1].performedOn !== entry.performedOn) && (
              <h2 className="mb-3 flex items-center gap-2 text-xl">
                <CalendarDays size={17} className="text-primary" aria-hidden="true" />
                {journalCalendarDate(entry.performedOn)}
              </h2>
            )}
            <JournalCard
              entry={entry}
              open={() => setEntryId(entry.id)}
              filterPlan={() => navigate({ ...filters, therapyPlanId: entry.therapyPlanId, planItemId: '' })}
              filterItem={() =>
                navigate({ ...filters, therapyPlanId: entry.therapyPlanId, planItemId: entry.planItemId })
              }
            />
          </section>
        ))}
      </div>
      {(list.totalPages > 1 || page > 1) && (
        <Pages
          page={page}
          pages={list.totalPages}
          next={list.hasNext}
          busy={false}
          onChange={(next) => navigate(filters, next)}
        />
      )}
      {entryId && (
        <EntryDetails
          key={entryId}
          patientId={patientId}
          entryId={entryId}
          onClose={() => setEntryId(null)}
          onDenied={onDenied}
        />
      )}
    </div>
  );
}

function JournalCard({
  entry,
  open,
  filterPlan,
  filterItem,
}: {
  entry: JournalEntry;
  open: () => void;
  filterPlan: () => void;
  filterItem: () => void;
}) {
  return (
    <article className="min-w-0 overflow-hidden rounded-2xl border bg-card shadow-sm">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b bg-muted/15 p-5">
        <div className="min-w-0">
          <p className="mb-2 text-xs text-muted-foreground [overflow-wrap:anywhere]">
            {entry.planName || 'Plan terapii'} · wersja {entry.planVersionNumber}
          </p>
          <h3 className="text-2xl [overflow-wrap:anywhere]">{entry.exercise.name}</h3>
        </div>
        <JournalStatusBadge status={entry.status} />
      </header>
      <div className="grid min-w-0 gap-4 p-5 sm:grid-cols-2">
        <div className="rounded-xl bg-muted/25 p-4 text-sm leading-7">
          <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Zalecone w tej wersji
          </p>
          <JournalDose
            sets={entry.exercise.sets}
            repetitions={entry.exercise.repetitions}
            durationSeconds={entry.exercise.durationSeconds}
          />
        </div>
        <div className="rounded-xl bg-primary/5 p-4 text-sm leading-7">
          <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-primary">
            Zgłoszone przez pacjenta
          </p>
          <JournalDose
            sets={entry.actualSets}
            repetitions={entry.actualRepetitions}
            durationSeconds={entry.actualDurationSeconds}
          />
        </div>
        <p className="text-sm">
          <span className="text-muted-foreground">Ocena bólu: </span>
          {entry.painLevel === null
            ? 'Nie oceniono'
            : `${entry.painLevel}/10${entry.painLevel === 0 ? ' — brak bólu' : ''}`}
        </p>
        <p className="line-clamp-3 whitespace-pre-wrap text-sm leading-7 [overflow-wrap:anywhere]">
          {entry.comment || 'Bez komentarza.'}
        </p>
      </div>
      <footer className="flex flex-wrap gap-2 border-t p-4">
        <Button type="button" variant="outline" size="sm" onClick={open}>
          Szczegóły wpisu
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={filterPlan}>
          Tylko ten plan
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={filterItem}>
          Tylko ta pozycja
        </Button>
      </footer>
    </article>
  );
}

function ReadFailure({ error, retry }: { error: unknown; retry: () => void }) {
  return (
    <section className="space-y-4 rounded-2xl border bg-card p-6">
      <p role="alert" className="text-sm text-destructive">
        {therapistJournalError(error)}
      </p>
      <Button type="button" variant="outline" onClick={retry}>
        Pobierz ponownie
      </Button>
    </section>
  );
}

function EntryDetails({
  patientId,
  entryId,
  onClose,
  onDenied,
}: {
  patientId: string;
  entryId: string;
  onClose: () => void;
  onDenied: (error: unknown) => void;
}) {
  const load = useCallback(
    async (signal: AbortSignal) => {
      try {
        return await therapistJournalApi.entry(patientId, entryId, signal);
      } catch (error) {
        if (!signal.aborted && accessDenied(error)) onDenied(error);
        throw error;
      }
    },
    [patientId, entryId, onDenied],
  );
  const query = usePatientContent(load);
  const entry = query.data;
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-4xl overflow-y-auto rounded-2xl">
        <DialogHeader>
          <DialogTitle>Szczegóły wpisu dziennika</DialogTitle>
          <DialogDescription>
            Historyczne zalecenia i bieżąca deklaracja pacjenta. Podgląd nie umożliwia edycji.
          </DialogDescription>
        </DialogHeader>
        {query.loading ? (
          <LoadingState title="Pobieranie wpisu…" />
        ) : query.error ? (
          <ReadFailure error={query.error} retry={query.reload} />
        ) : (
          entry && (
            <div className="space-y-5">
              <div>
                <h2 className="text-3xl [overflow-wrap:anywhere]">{entry.exercise.name}</h2>
                <p className="mt-2 text-sm text-muted-foreground [overflow-wrap:anywhere]">
                  {entry.planName || 'Plan terapii'} · wersja {entry.planVersionNumber}
                </p>
              </div>
              <div className="grid min-w-0 gap-4 md:grid-cols-2">
                <section className="min-w-0 space-y-3 rounded-2xl border p-5">
                  <h3 className="text-2xl">Zalecenia z zapisanej wersji</h3>
                  <p className="whitespace-pre-wrap text-sm leading-7 [overflow-wrap:anywhere]">
                    {entry.exercise.instructions || 'Bez instrukcji tekstowej.'}
                  </p>
                  <p className="text-sm">
                    <JournalDose
                      sets={entry.exercise.sets}
                      repetitions={entry.exercise.repetitions}
                      durationSeconds={entry.exercise.durationSeconds}
                    />
                  </p>
                  {entry.exercise.frequency && (
                    <p className="text-sm">Częstotliwość: {entry.exercise.frequency}</p>
                  )}
                  {entry.exercise.notes && (
                    <p className="whitespace-pre-wrap text-sm leading-7 [overflow-wrap:anywhere]">
                      {entry.exercise.notes}
                    </p>
                  )}
                </section>
                <section className="min-w-0 space-y-3 rounded-2xl border border-primary/20 bg-primary/5 p-5">
                  <h3 className="text-2xl">Zgłoszenie pacjenta</h3>
                  <JournalReportView entry={entry} />
                </section>
              </div>
              <dl className="grid gap-3 border-t pt-4 text-xs text-muted-foreground sm:grid-cols-3">
                <div>
                  <dt>Podstawa publikacji</dt>
                  <dd className="mt-1">{recordDate(entry.publishedAt)}</dd>
                </div>
                <div>
                  <dt>Dodano wpis</dt>
                  <dd className="mt-1">{recordDate(entry.createdAt)}</dd>
                </div>
                <div>
                  <dt>Ostatnia edycja</dt>
                  <dd className="mt-1">{entry.updatedAt ? recordDate(entry.updatedAt) : 'Nie edytowano'}</dd>
                </div>
              </dl>
            </div>
          )
        )}
        <div className="flex justify-end border-t pt-4">
          <Button type="button" variant="outline" onClick={onClose}>
            Zamknij szczegóły
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
