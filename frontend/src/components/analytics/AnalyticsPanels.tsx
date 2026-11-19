'use client';

import { useCallback, useId, useState, type ReactNode } from 'react';
import dynamic from 'next/dynamic';
import { Activity, BarChart3, CalendarDays, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { usePatientContent } from '@/hooks/use-patient-content';
import {
  analyticsApi,
  analyticsError,
  analyticsRangeError,
  currentMonth,
  formatPain,
  type AnalyticsRange,
  type PainTrend,
} from '@/lib/analytics-api';

const PainChart = dynamic(() => import('./PainChart'), {
  ssr: false,
  loading: () => (
    <p role="status" className="py-6 text-sm text-muted-foreground">
      Wczytywanie wykresu…
    </p>
  ),
});
type Selection = { mode: 'week' | 'month' | 'custom'; range: AnalyticsRange | null };
const emptyRange = { from: '', to: '' };
const formatDate = (value: string) => value.split('-').reverse().join('.');

function AnalyticsPanel({
  title,
  description,
  kind,
  children,
}: {
  title: string;
  description: string;
  kind: 'practice' | 'pain';
  children: (range: AnalyticsRange | null) => ReactNode;
}) {
  const heading = useId();
  const [selection, setSelection] = useState<Selection>({ mode: 'week', range: null });
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(emptyRange);
  const validation = analyticsRangeError(draft);
  const Icon = kind === 'practice' ? BarChart3 : Activity;
  return (
    <section aria-labelledby={heading} className="min-w-0 space-y-5 rounded-2xl border bg-card p-4 sm:p-6">
      <header className="flex gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <Icon size={20} aria-hidden="true" />
        </span>
        <div>
          <h2 id={heading} className="text-3xl">
            {title}
          </h2>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">{description}</p>
        </div>
      </header>
      <div className="flex flex-wrap gap-2" role="group" aria-label="Zakres danych">
        <Button
          type="button"
          size="sm"
          variant={selection.mode === 'week' && !editing ? 'secondary' : 'outline'}
          aria-pressed={selection.mode === 'week' && !editing}
          onClick={() => {
            setEditing(false);
            setSelection({ mode: 'week', range: null });
          }}
        >
          Bieżący tydzień
        </Button>
        <Button
          type="button"
          size="sm"
          variant={selection.mode === 'month' && !editing ? 'secondary' : 'outline'}
          aria-pressed={selection.mode === 'month' && !editing}
          onClick={() => {
            setEditing(false);
            setSelection({ mode: 'month', range: currentMonth() });
          }}
        >
          Bieżący miesiąc
        </Button>
        <Button
          type="button"
          size="sm"
          variant={editing ? 'secondary' : 'outline'}
          aria-expanded={editing}
          onClick={() => {
            if (!editing) setDraft(selection.range ?? emptyRange);
            setEditing(!editing);
          }}
        >
          Własne daty
        </Button>
      </div>
      {editing && (
        <form
          className="space-y-3 rounded-xl border bg-muted/20 p-3"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            if (!validation) {
              setSelection({ mode: 'custom', range: draft });
              setEditing(false);
            }
          }}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="min-w-0 space-y-1 text-sm">
              <span>Od</span>
              <input
                type="date"
                min="1900-01-01"
                max="9999-12-30"
                className="block h-10 w-full min-w-0 rounded-md border bg-background px-2"
                value={draft.from}
                onChange={(e) => setDraft({ ...draft, from: e.target.value })}
              />
            </label>
            <label className="min-w-0 space-y-1 text-sm">
              <span>Do</span>
              <input
                type="date"
                min="1900-01-01"
                max="9999-12-30"
                className="block h-10 w-full min-w-0 rounded-md border bg-background px-2"
                value={draft.to}
                onChange={(e) => setDraft({ ...draft, to: e.target.value })}
              />
            </label>
          </div>
          <p className="text-xs text-muted-foreground">Obie granice są włączne. Maksymalnie 366 dni.</p>
          {validation && (draft.from || draft.to) && (
            <p role="alert" className="text-sm text-destructive">
              {validation}
            </p>
          )}
          <Button type="submit" size="sm" disabled={Boolean(validation)}>
            Zastosuj zakres
          </Button>
        </form>
      )}
      <div key={selection.range ? `${selection.range.from}:${selection.range.to}` : 'week'}>
        {children(selection.range)}
      </div>
    </section>
  );
}

function Result({
  loading,
  error,
  reload,
  children,
}: {
  loading: boolean;
  error: unknown;
  reload: () => void;
  children: ReactNode;
}) {
  if (loading)
    return (
      <p role="status" className="py-5 text-sm text-muted-foreground">
        Pobieranie danych analitycznych…
      </p>
    );
  if (error)
    return (
      <div className="space-y-3">
        <p role="alert" className="text-sm text-destructive">
          {analyticsError(error)}
        </p>
        <Button type="button" variant="outline" size="sm" onClick={reload}>
          Spróbuj ponownie
        </Button>
      </div>
    );
  return children;
}

function RangeLabel({ from, to, timeZone }: { from: string; to: string; timeZone: string }) {
  return (
    <p className="text-xs text-muted-foreground">
      {formatDate(from)} – {formatDate(to)} · {timeZone}
    </p>
  );
}

export function PracticeAnalytics() {
  return (
    <AnalyticsPanel
      title="Statystyki gabinetu"
      description="Karty pacjentów i wizyty według ich aktualnego statusu."
      kind="practice"
    >
      {(range) => <PracticeData range={range} />}
    </AnalyticsPanel>
  );
}

function PracticeData({ range }: { range: AnalyticsRange | null }) {
  const load = useCallback((signal: AbortSignal) => analyticsApi.dashboard(range, signal), [range]);
  const result = usePatientContent(load);
  const data = result.data;
  return (
    <Result {...result}>
      {data && (
        <div className="space-y-4">
          <RangeLabel {...data} />
          <dl className="grid grid-cols-2 gap-3">
            <div className="space-y-2 rounded-xl border bg-primary/5 p-3 sm:p-4">
              <dt className="flex items-center gap-2 text-sm">
                <Users size={16} aria-hidden="true" />
                Aktywni pacjenci
              </dt>
              <dd className="text-3xl font-semibold tabular-nums">{data.activePatientCount}</dd>
              <dd className="text-xs text-muted-foreground">Stan bieżący, niezależny od zakresu.</dd>
            </div>
            <div className="space-y-2 rounded-xl border bg-muted/25 p-3 sm:p-4">
              <dt className="flex items-center gap-2 text-sm">
                <CalendarDays size={16} aria-hidden="true" />
                Wszystkie wizyty
              </dt>
              <dd className="text-3xl font-semibold tabular-nums">{data.appointments.total}</dd>
              <dd className="text-xs text-muted-foreground">W wybranym zakresie.</dd>
            </div>
          </dl>
          <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            {(
              [
                ['Zaplanowane', 'scheduled'],
                ['Odbyte', 'completed'],
                ['Odwołane', 'cancelled'],
                ['Nieobecności', 'noShow'],
              ] as const
            ).map(([label, key]) => (
              <div key={key} className="rounded-xl border p-3">
                <dt className="text-sm text-muted-foreground">{label}</dt>
                <dd className="mt-1 text-xl font-semibold tabular-nums">{data.appointments[key]}</dd>
              </div>
            ))}
          </dl>
          {data.appointments.total === 0 && (
            <p className="text-sm text-muted-foreground">Brak wizyt w wybranym zakresie.</p>
          )}
        </div>
      )}
    </Result>
  );
}

export function PatientPainAnalytics({ patientId }: { patientId: string }) {
  return (
    <AnalyticsPanel
      title="Zgłaszany ból"
      description="Dzienne średnie ocen 0–10 z dziennika ćwiczeń. Wykres nie ocenia skuteczności terapii."
      kind="pain"
    >
      {(range) => <PainData range={range} patientId={patientId} />}
    </AnalyticsPanel>
  );
}

function PainData({ range, patientId }: { range: AnalyticsRange | null; patientId: string }) {
  const load = useCallback(
    (signal: AbortSignal) => analyticsApi.pain(patientId, range, signal),
    [patientId, range],
  );
  const result = usePatientContent(load);
  return <Result {...result}>{result.data && <PainDataView data={result.data} />}</Result>;
}

function PainDataView({ data }: { data: PainTrend }) {
  const hasRatings = data.points.some((point) => point.averagePainLevel !== null);
  return (
    <div className="space-y-4">
      <RangeLabel {...data} />
      {hasRatings ? (
        <PainChart data={data} />
      ) : (
        <p className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
          Brak ocen bólu w wybranym zakresie.
        </p>
      )}
      {data.points.length > 0 && (
        <details className="rounded-xl border p-3">
          <summary className="cursor-pointer text-sm font-semibold">Tabela ocen bólu</summary>
          <div className="mt-3 max-h-72 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-muted/40">
                <tr>
                  <th scope="col" className="p-2">
                    Data
                  </th>
                  <th scope="col" className="p-2">
                    Średnia
                  </th>
                  <th scope="col" className="p-2">
                    Liczba ocen
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {data.points.map((point) => (
                  <tr key={point.date}>
                    <th scope="row" className="p-2 font-medium">
                      {formatDate(point.date)}
                    </th>
                    <td className="p-2">{formatPain(point.averagePainLevel)}</td>
                    <td className="p-2">{point.ratingCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </div>
  );
}
