import { CheckCircle2, CircleDashed, MinusCircle } from 'lucide-react';
import { journalStatuses, type JournalEntry, type JournalStatus } from '@/lib/exercise-journal-api';
import { recordDate } from '@/lib/medical-records-api';

export function JournalStatusBadge({ status }: { status: JournalStatus }) {
  const Icon = status === 'Completed' ? CheckCircle2 : status === 'Partial' ? CircleDashed : MinusCircle;
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold ${status === 'Completed' ? 'bg-primary/10 text-primary' : status === 'Partial' ? 'bg-amber-50 text-amber-900' : 'bg-muted text-muted-foreground'}`}
    >
      <Icon size={14} aria-hidden="true" />
      {journalStatuses[status]}
    </span>
  );
}

export function JournalDose({
  sets,
  repetitions,
  durationSeconds,
}: {
  sets: number | null;
  repetitions: number | null;
  durationSeconds: number | null;
}) {
  const values = [
    sets === null ? null : `Serie: ${sets}`,
    repetitions === null ? null : `Powtórzenia: ${repetitions}`,
    durationSeconds === null ? null : `Czas: ${durationSeconds} s`,
  ].filter(Boolean);
  return <span>{values.join(' · ') || 'Nie podano parametrów'}</span>;
}

export function JournalReportView({ entry }: { entry: JournalEntry }) {
  return (
    <div className="space-y-3 text-sm leading-6">
      <div className="flex flex-wrap items-center gap-3">
        <strong>{entry.performedOn}</strong>
        <JournalStatusBadge status={entry.status} />
      </div>
      <div>
        <span className="text-muted-foreground">Zgłoszone wykonanie: </span>
        <JournalDose
          sets={entry.actualSets}
          repetitions={entry.actualRepetitions}
          durationSeconds={entry.actualDurationSeconds}
        />
      </div>
      <p>
        <span className="text-muted-foreground">Ocena bólu: </span>
        {entry.painLevel === null
          ? 'Nie oceniono'
          : `${entry.painLevel}/10${entry.painLevel === 0 ? ' — brak bólu' : ''}`}
      </p>
      <p className="whitespace-pre-wrap [overflow-wrap:anywhere]">{entry.comment || 'Bez komentarza.'}</p>
    </div>
  );
}

export function JournalEntryView({ entry }: { entry: JournalEntry }) {
  return (
    <div className="min-w-0 space-y-4">
      <JournalReportView entry={entry} />
      <details className="rounded-xl border bg-muted/15 p-4 text-sm">
        <summary className="cursor-pointer font-medium">Zalecenia z zapisanej wersji planu</summary>
        <div className="mt-3 space-y-3 leading-6">
          <p className="text-xs text-muted-foreground">
            {entry.planName || 'Plan terapii'} · wersja {entry.planVersionNumber}. To historyczna kopia, nie
            aktualna biblioteka.
          </p>
          <p className="whitespace-pre-wrap [overflow-wrap:anywhere]">
            {entry.exercise.instructions || 'Bez instrukcji tekstowej.'}
          </p>
          <JournalDose
            sets={entry.exercise.sets}
            repetitions={entry.exercise.repetitions}
            durationSeconds={entry.exercise.durationSeconds}
          />
          {entry.exercise.frequency && <p>Częstotliwość: {entry.exercise.frequency}</p>}
          {entry.exercise.notes && (
            <p className="whitespace-pre-wrap [overflow-wrap:anywhere]">{entry.exercise.notes}</p>
          )}
          <p className="text-xs text-muted-foreground">
            Zapisano: {recordDate(entry.createdAt)}
            {entry.updatedAt ? ` · Edytowano: ${recordDate(entry.updatedAt)}` : ''}
          </p>
        </div>
      </details>
    </div>
  );
}
