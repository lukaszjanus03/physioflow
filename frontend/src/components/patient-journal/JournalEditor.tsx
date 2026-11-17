'use client';

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/primitives';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { patientSharingApi } from '@/lib/patient-sharing-api';
import {
  journalApi,
  journalError,
  journalErrorCode,
  journalHttpStatus,
  journalStatuses,
  type CreateJournalRequest,
  type JournalEntry,
  type JournalExercise,
} from '@/lib/exercise-journal-api';
import {
  journalForm,
  journalPayload,
  validateJournal,
  warsawDate,
  type JournalErrors,
  type JournalForm,
} from '@/lib/exercise-journal-form';
import { useJournalNavigation } from './JournalNavigation';
import { JournalDose, JournalReportView } from './JournalEntryView';

export type JournalTarget = {
  therapyPlanId: string;
  planVersionNumber: number;
  planItemId: string;
  publishedAt: string;
  planName: string | null;
  exercise: JournalExercise;
};
type Props = {
  source: { target: JournalTarget; entry?: never } | { entry: JournalEntry; target?: never };
  onClose: () => void;
  onSaved: (entry: JournalEntry) => void;
};

export function JournalEditor({ source, onClose, onSaved }: Props) {
  const id = useId();
  const [base, setBase] = useState(source.entry);
  const [today] = useState(() => warsawDate());
  const [form, setForm] = useState(() => journalForm(source.entry, today));
  const [errors, setErrors] = useState<JournalErrors>({});
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [frozen, setFrozen] = useState<CreateJournalRequest | null>(null);
  const [blocked, setBlocked] = useState(false);
  const [mustCheckPlan, setMustCheckPlan] = useState(false);
  const [mustRead, setMustRead] = useState(false);
  const [latest, setLatest] = useState<JournalEntry | null>(null);
  const [compared, setCompared] = useState(false);
  const request = useRef<AbortController | null>(null);
  const { setDraft, confirmLeave } = useJournalNavigation();
  const [target] = useState(source.target);
  const exercise = base?.exercise ?? target!.exercise;
  const publishedAt = base?.publishedAt ?? target!.publishedAt;
  const dirty = JSON.stringify(form) !== JSON.stringify(journalForm(base, today)) || frozen !== null;

  useEffect(() => {
    setDraft({ dirty, busy });
    return () => setDraft({ dirty: false, busy: false });
  }, [dirty, busy, setDraft]);
  useEffect(
    () => () => {
      request.current?.abort();
    },
    [],
  );

  async function run(operation: (signal: AbortSignal) => Promise<void>) {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    try {
      await operation(controller.signal);
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function save() {
    if (busy || blocked || mustRead || mustCheckPlan) return;
    const nextErrors = frozen ? {} : validateJournal(form, publishedAt);
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length) return;
    await run(async (signal) => {
      setMessage('');
      let body = frozen;
      if (target && !body) {
        body = {
          ...journalPayload(form),
          requestId: crypto.randomUUID(),
          therapyPlanId: target.therapyPlanId,
          planVersionNumber: target.planVersionNumber,
          planItemId: target.planItemId,
        };
        setFrozen(body);
      }
      try {
        const saved = base
          ? await journalApi.update(base.id, base.version, journalPayload(form), signal)
          : await journalApi.create(body!, signal);
        if (signal.aborted) return;
        toast.success(base ? 'Wpis został zaktualizowany.' : 'Zapisano wykonanie w dzienniku.');
        onSaved(saved);
      } catch (error) {
        if (signal.aborted) return;
        setMessage(journalError(error));
        const status = journalHttpStatus(error);
        const code = journalErrorCode(error);
        if (base) {
          setMustRead(status !== 400 && status !== 401 && status !== 403);
          if (status === 401 || status === 403) setBlocked(true);
        } else {
          if (status === 400) setFrozen(null);
          if (code === 'concurrent_modification') setMustCheckPlan(true);
          if (
            [401, 403, 404].includes(status ?? 0) ||
            code === 'idempotency_key_reused' ||
            code === 'journal_entry_deleted'
          )
            setBlocked(true);
        }
      }
    });
  }

  async function refreshState() {
    await run(async (signal) => {
      try {
        if (base) {
          const current = await journalApi.get(base.id, signal);
          if (signal.aborted) return;
          setLatest(current);
          setCompared(false);
        } else if (target) {
          const current = await patientSharingApi.plan(target.therapyPlanId, signal);
          if (signal.aborted) return;
          const same =
            current.versionNumber === target.planVersionNumber &&
            current.publishedAt === target.publishedAt &&
            current.content.exercises.some((item) => item.id === target.planItemId);
          setMustCheckPlan(!same);
          setBlocked(!same);
          setMessage(
            same
              ? 'Udostępnienie jest aktualne. Możesz świadomie ponowić identyczne zgłoszenie.'
              : 'Udostępnienie zmieniło się. Zamknij formularz, sprawdź dziennik i odśwież plan przed nowym zgłoszeniem.',
          );
        }
      } catch (error) {
        if (!signal.aborted) setMessage(journalError(error));
      }
    });
  }

  const fieldsLocked = busy || blocked || mustRead || frozen !== null;
  function field(key: keyof JournalForm, label: string, input: ReactNode) {
    return (
      <div key={key} className="min-w-0 space-y-2">
        <label htmlFor={`${id}-${key}`} className="block text-sm font-medium">
          {label}
        </label>
        {input}
        {errors[key] && (
          <p id={`${id}-${key}-error`} className="text-xs text-destructive">
            {errors[key]}
          </p>
        )}
      </div>
    );
  }
  function change(key: keyof JournalForm, value: string) {
    setForm((old) => ({ ...old, [key]: value }));
    setErrors({});
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) confirmLeave(onClose);
      }}
    >
      <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-3xl overflow-y-auto rounded-2xl">
        <DialogHeader>
          <DialogTitle>{base ? 'Edytuj wpis dziennika' : 'Zapisz wykonanie ćwiczenia'}</DialogTitle>
          <DialogDescription>
            Twoje zgłoszenie, nie ocena medyczna. Możesz zapisać kilka wykonań tego samego ćwiczenia jednego
            dnia.
          </DialogDescription>
        </DialogHeader>
        <div className="rounded-xl border bg-primary/5 p-4">
          <p className="font-semibold [overflow-wrap:anywhere]">{exercise.name}</p>
          <p className="mt-1 text-xs text-muted-foreground">
            {base?.planName ?? target?.planName ?? 'Plan terapii'} · wersja{' '}
            {base?.planVersionNumber ?? target?.planVersionNumber}
          </p>
          <p className="mt-3 text-sm">
            <strong>Zalecone: </strong>
            <JournalDose
              sets={exercise.sets}
              repetitions={exercise.repetitions}
              durationSeconds={exercise.durationSeconds}
            />
          </p>
        </div>
        <form
          noValidate
          className="space-y-5"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          <fieldset disabled={fieldsLocked} className="grid min-w-0 gap-4 sm:grid-cols-2">
            <legend className="sr-only">Zgłoszone wykonanie</legend>
            {field(
              'performedOn',
              'Data wykonania',
              <Input
                id={`${id}-performedOn`}
                type="date"
                min={warsawDate(new Date(publishedAt))}
                max={today}
                value={form.performedOn}
                aria-invalid={Boolean(errors.performedOn)}
                aria-describedby={errors.performedOn ? `${id}-performedOn-error` : undefined}
                onChange={(e) => change('performedOn', e.target.value)}
              />,
            )}
            {field(
              'status',
              'Status realizacji',
              <select
                id={`${id}-status`}
                className="h-11 w-full rounded-lg border bg-background px-3"
                value={form.status}
                aria-invalid={Boolean(errors.status)}
                onChange={(event) => {
                  const status = event.target.value as JournalForm['status'];
                  setForm((old) => ({
                    ...old,
                    status,
                    ...(status === 'Skipped'
                      ? { actualSets: '', actualRepetitions: '', actualDurationSeconds: '' }
                      : {}),
                  }));
                  setErrors({});
                }}
              >
                {Object.entries(journalStatuses).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>,
            )}
            {form.status !== 'Skipped' && (
              <>
                {(['actualSets', 'actualRepetitions', 'actualDurationSeconds'] as const).map((key) =>
                  field(
                    key,
                    {
                      actualSets: 'Wykonane serie (opcjonalnie)',
                      actualRepetitions: 'Wykonane powtórzenia (opcjonalnie)',
                      actualDurationSeconds: 'Wykonany czas w sekundach (opcjonalnie)',
                    }[key],
                    <Input
                      id={`${id}-${key}`}
                      type="number"
                      min={1}
                      max={key === 'actualSets' ? 100 : key === 'actualRepetitions' ? 1000 : 86400}
                      step={1}
                      value={form[key]}
                      aria-invalid={Boolean(errors[key])}
                      aria-describedby={errors[key] ? `${id}-${key}-error` : undefined}
                      onChange={(e) => {
                        const value = e.target.value;
                        setForm((old) => ({
                          ...old,
                          [key]: value,
                          ...(value && key === 'actualRepetitions'
                            ? { actualDurationSeconds: '' }
                            : value && key === 'actualDurationSeconds'
                              ? { actualRepetitions: '' }
                              : {}),
                        }));
                        setErrors({});
                      }}
                    />,
                  ),
                )}
                <p className="text-xs text-muted-foreground sm:col-span-2">
                  Podaj powtórzenia albo czas. Wpisanie jednej miary wyczyści drugą. Parametry są opcjonalne i
                  nie muszą odpowiadać zaleceniom.
                </p>
              </>
            )}
            {field(
              'painLevel',
              'Ocena bólu (opcjonalnie)',
              <select
                id={`${id}-painLevel`}
                className="h-11 w-full rounded-lg border bg-background px-3"
                value={form.painLevel}
                onChange={(e) => change('painLevel', e.target.value)}
              >
                <option value="">Nie oceniam</option>
                {Array.from({ length: 11 }, (_, value) => (
                  <option key={value} value={value}>
                    {value === 0 ? '0 — brak bólu' : `${value}/10`}
                  </option>
                ))}
              </select>,
            )}
            <div className="sm:col-span-2">
              {field(
                'comment',
                'Komentarz (opcjonalnie)',
                <Textarea
                  id={`${id}-comment`}
                  value={form.comment}
                  rows={4}
                  maxLength={2000}
                  aria-invalid={Boolean(errors.comment)}
                  onChange={(e) => change('comment', e.target.value)}
                />,
              )}
              <p className="mt-2 text-xs text-muted-foreground">
                {form.comment.length}/2000 znaków. Pusty komentarz i brak oceny są dozwolone.
              </p>
            </div>
          </fieldset>
          {Object.keys(errors).length > 0 && (
            <p role="alert" className="text-sm text-destructive">
              Popraw zaznaczone pola przed zapisem.
            </p>
          )}
          {message && (
            <p
              role="alert"
              className="rounded-xl border border-amber-200 bg-amber-50/50 p-4 text-sm leading-6"
            >
              {message}
            </p>
          )}
          {frozen && !busy && !blocked && (
            <p className="text-xs leading-6 text-muted-foreground">
              Zachowano oryginalną treść i identyfikator zgłoszenia. Ponowienie nie tworzy drugiego wpisu.
              Przed porzuceniem niepewnego zapisu sprawdź historię, zanim zgłosisz to samo wykonanie jako
              nowe.
            </p>
          )}
          {(mustRead || mustCheckPlan) && !latest && (
            <Button type="button" variant="outline" disabled={busy} onClick={() => void refreshState()}>
              {base ? 'Pobierz aktualny wpis do porównania' : 'Sprawdź aktualne udostępnienie'}
            </Button>
          )}
          {latest && (
            <section className="space-y-4 rounded-xl border border-amber-300 p-4">
              <h3 className="text-2xl">Aktualny wpis na serwerze</h3>
              <JournalReportView entry={latest} />
              <p className="text-sm">
                Twoja treść pozostała w formularzu powyżej. Żaden z poniższych przycisków nie zapisuje zmian.
              </p>
              <label className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={compared}
                  onChange={(e) => setCompared(e.target.checked)}
                  className="mt-1"
                />
                Porównałem obie treści i świadomie wybieram dane do dalszej edycji.
              </label>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  disabled={!compared || busy}
                  onClick={() => {
                    setBase(latest);
                    setForm(journalForm(latest));
                    setLatest(null);
                    setMustRead(false);
                    setErrors({});
                    setMessage('Przyjęto treść serwera.');
                  }}
                >
                  Przyjmij dane serwera
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  disabled={!compared || busy}
                  onClick={() => {
                    setBase(latest);
                    setLatest(null);
                    setMustRead(false);
                    setMessage(
                      'Zachowano formularz. Kliknij Zapisz zmiany, jeśli chcesz zastąpić aktualny wpis.',
                    );
                  }}
                >
                  Zachowaj mój formularz
                </Button>
              </div>
            </section>
          )}
          <div className="flex flex-wrap justify-end gap-3 border-t pt-4">
            <Button type="button" variant="outline" disabled={busy} onClick={() => confirmLeave(onClose)}>
              Zamknij
            </Button>
            <Button type="submit" disabled={busy || blocked || mustRead || mustCheckPlan}>
              {busy
                ? 'Zapisywanie…'
                : base
                  ? 'Zapisz zmiany'
                  : frozen
                    ? 'Ponów identyczne zgłoszenie'
                    : 'Zapisz wykonanie'}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
