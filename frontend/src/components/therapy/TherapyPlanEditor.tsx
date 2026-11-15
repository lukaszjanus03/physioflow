'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useRef, useState } from 'react';
import Link from 'next/link';
import {
  ArrowDown,
  ArrowUp,
  Plus,
  Trash2,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import ExerciseLibrary from '@/components/therapy/ExerciseLibrary';
import {
  Confirm,
  Field,
  PlanView,
  StatusBadge,
  TherapyError,
} from '@/components/therapy/TherapyUI';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { patientsApi } from '@/lib/api';
import {
  planErrors,
  planForm,
  planPayload,
} from '@/lib/therapy-form';
import type {
  ItemForm,
  PlanForm,
} from '@/lib/therapy-form';
import {
  therapyApi,
  therapyError,
  therapyFieldErrors,
  therapyId,
  therapyOptions,
  therapyStatus,
} from '@/lib/therapy-api';
import type {
  Exercise,
  PlanContent,
  TherapyPlan,
} from '@/lib/therapy-api';
import { recordDate } from '@/lib/medical-records-api';
import { useAuthStore } from '@/stores/auth-store';

export default function TherapyPlanEditor({
  patientId,
  planId,
}: {
  patientId: string;
  planId: string;
}) {
  const accountId = useAuthStore(
    (state) => state.userId,
  );

  const query = useQuery({
    queryKey: [
      'therapy-plan',
      accountId,
      planId,
      patientId,
    ],
    queryFn: async ({ signal }) => {
      const plan = await therapyApi.plan(
        planId,
        signal,
      );

      if (plan.patientId !== patientId) {
        throw new Error('invalid-patient');
      }

      const patient =
        await patientsApi.getById(patientId);

      return { plan, patient };
    },
    enabled:
      Boolean(accountId) &&
      therapyId(patientId) &&
      therapyId(planId),
    ...therapyOptions,
  });

  if (
    !therapyId(patientId) ||
    !therapyId(planId)
  ) {
    return (
      <p role="alert">
        Nieprawidłowy adres planu.
      </p>
    );
  }

  if (query.isError) {
    return (
      <TherapyError
        error={query.error}
        retry={() => void query.refetch()}
      />
    );
  }

  if (query.isPending) {
    return (
      <LoadingState title="Pobieranie planu…" />
    );
  }

  return (
    <Editor
      key={`${accountId}:${planId}`}
      initial={query.data.plan}
      accountId={accountId ?? ''}
      initialArchived={
        query.data.patient.isArchived
      }
      patientName={`${query.data.patient.firstName} ${query.data.patient.lastName}`}
    />
  );
}

type Action =
  | 'save'
  | 'correct'
  | 'activate'
  | 'complete'
  | 'archive';

const actionTitles = {
  correct: 'Zapisać korektę planu?',
  activate: 'Aktywować zapisany szkic?',
  complete: 'Zakończyć plan?',
  archive: 'Archiwizować plan?',
};

const actionTexts = {
  correct:
    'Powstanie kolejna aktywowana wersja. Poprzednia treść pozostanie w historii.',
  activate:
    'Powstanie wersja 1 zapisanej treści. Plan nie zostanie jeszcze udostępniony pacjentowi.',
  complete:
    'Zakończony plan będzie dostępny tylko do odczytu. Nie można go ponownie aktywować.',
  archive:
    'Plan pozostanie w historii, ale dalsza edycja i przywrócenie nie będą dostępne.',
};

function preview(form: PlanForm): PlanContent {
  const number = (value: string) =>
    value !== '' &&
    Number.isFinite(Number(value))
      ? Number(value)
      : null;

  return {
    name: form.name,
    goal: form.goal,
    startDate: form.startDate || null,
    endDate: form.endDate || null,
    generalRecommendations:
      form.generalRecommendations,
    exercises: form.exercises.map(
      (item, order) => ({
        ...item,
        id: item.id ?? item.key,
        order,
        sets: number(item.sets),
        repetitions: number(
          item.repetitions,
        ),
        durationSeconds: number(
          item.durationSeconds,
        ),
      }),
    ),
  };
}

function Editor({
  initial,
  accountId,
  initialArchived,
  patientName,
}: {
  initial: TherapyPlan;
  accountId: string;
  initialArchived: boolean;
  patientName: string;
}) {
  const client = useQueryClient();

  const [base, setBase] = useState(initial);
  const [form, setForm] = useState(() =>
    planForm(initial.content),
  );
  const [archived, setArchived] =
    useState(initialArchived);
  const [correcting, setCorrecting] =
    useState(false);
  const [reason, setReason] = useState('');
  const [step, setStep] = useState(0);
  const [picking, setPicking] =
    useState(false);
  const [history, setHistory] =
    useState(false);
  const [message, setMessage] = useState('');
  const [success, setSuccess] = useState('');
  const [errors, setErrors] =
    useState<Record<string, string>>({});
  const [remote, setRemote] =
    useState<TherapyPlan | null>(null);
  const [blocked, setBlocked] =
    useState(false);
  const [compared, setCompared] =
    useState(false);
  const [reading, setReading] =
    useState(false);
  const [confirmation, setConfirmation] =
    useState<Exclude<Action, 'save'> | null>(
      null,
    );

  const lock = useRef(false);

  const editing =
    !archived &&
    (base.status === 'Draft' ||
      (base.status === 'Active' &&
        correcting));

  const dirty =
    JSON.stringify(planPayload(form)) !==
      JSON.stringify(
        planPayload(planForm(base.content)),
      ) || reason.length > 0;

  async function fetchCurrent() {
    setReading(true);

    try {
      const current =
        await therapyApi.plan(base.id);

      if (
        current.patientId !== base.patientId
      ) {
        throw new Error('invalid-patient');
      }

      setRemote(current);
      setCompared(false);

      const patient =
        await patientsApi.getById(
          base.patientId,
        );

      setArchived(patient.isArchived);
    } catch (error) {
      setMessage(therapyError(error));
    } finally {
      setReading(false);
    }
  }

  const mutation = useMutation({
    mutationFn: (action: Action) =>
      action === 'save'
        ? therapyApi.writePlan(
            base,
            planPayload(form),
          )
        : action === 'correct'
          ? therapyApi.writePlan(
              base,
              planPayload(form),
              reason,
            )
          : therapyApi.transition(
              base,
              action,
            ),
    retry: false,
    onSuccess: async (plan, action) => {
      setBase(plan);
      setForm(planForm(plan.content));
      setReason('');
      setCorrecting(false);
      setBlocked(false);
      setRemote(null);
      setConfirmation(null);
      setErrors({});
      setMessage('');

      setSuccess(
        action === 'save'
          ? 'Szkic zapisany.'
          : 'Zmiana planu została zapisana.',
      );

      await Promise.all([
        client.invalidateQueries({
          queryKey: [
            'therapy-plans',
            accountId,
            base.patientId,
          ],
        }),
        client.invalidateQueries({
          queryKey: [
            'therapy-plan-history',
            accountId,
            base.id,
          ],
        }),
      ]);
    },
    onError: async (error) => {
      setConfirmation(null);
      setMessage(therapyError(error));
      setErrors(therapyFieldErrors(error));

      if (therapyStatus(error) === 409) {
        setBlocked(true);
        await fetchCurrent();
      }
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  const busy =
    mutation.isPending || reading;

  const guard = useUnsavedChanges(
    dirty,
    busy,
  );

  function allowed(action: Action) {
    return (
      !archived &&
      (action === 'save'
        ? base.status === 'Draft'
        : action === 'correct'
          ? base.status === 'Active' &&
            correcting
          : action === 'activate'
            ? base.status === 'Draft'
            : action === 'complete'
              ? base.status === 'Active'
              : base.status !== 'Archived')
    );
  }

  function validate(action: Action) {
    if (!allowed(action)) {
      setMessage(
        'Ta operacja nie jest dostępna dla aktualnego statusu lub archiwalnej karty.',
      );
      return false;
    }

    if (
      [
        'activate',
        'complete',
        'archive',
      ].includes(action) &&
      dirty
    ) {
      setMessage(
        'Najpierw zapisz albo odrzuć lokalne zmiany.',
      );
      return false;
    }

    const next =
      action === 'complete' ||
      action === 'archive'
        ? {}
        : planErrors(
            form,
            base,
            action !== 'save',
            action === 'correct'
              ? reason
              : undefined,
          );

    setErrors(next);

    if (Object.keys(next).length) {
      setStep(
        Object.keys(next).some((key) =>
          key.startsWith('exercises'),
        )
          ? 1
          : next.correctionReason
            ? 2
            : 0,
      );

      setMessage(
        'Sprawdź pola i parametry ćwiczeń. Błędy są opisane przy polach.',
      );
      return false;
    }

    return true;
  }

  function send(action: Action) {
    if (
      lock.current ||
      busy ||
      blocked ||
      remote ||
      !validate(action)
    ) {
      return;
    }

    lock.current = true;
    setMessage('');
    setSuccess('');
    mutation.mutate(action);
  }

  function request(
    action: Exclude<Action, 'save'>,
  ) {
    if (
      !busy &&
      !blocked &&
      !remote &&
      validate(action)
    ) {
      setConfirmation(action);
    }
  }

  function accept(keep: boolean) {
    if (
      !remote ||
      busy ||
      (keep && !compared)
    ) {
      return;
    }

    if (!keep) {
      setForm(planForm(remote.content));
      setReason('');
      setCorrecting(false);
    } else {
      setCorrecting(
        remote.status === 'Active',
      );
    }

    setBase(remote);
    setRemote(null);
    setBlocked(false);
    setMessage('');
    setErrors({});
    setSuccess('');
  }

  function patchItem(
    key: string,
    patch: Partial<ItemForm>,
  ) {
    setForm((old) => ({
      ...old,
      exercises: old.exercises.map((item) =>
        item.key === key
          ? { ...item, ...patch }
          : item,
      ),
    }));
    setSuccess('');
  }

  function move(
    key: string,
    direction: number,
  ) {
    setForm((old) => {
      const items = [...old.exercises];
      const index = items.findIndex(
        (item) => item.key === key,
      );
      const target = index + direction;

      if (
        index < 0 ||
        target < 0 ||
        target >= items.length
      ) {
        return old;
      }

      [items[index], items[target]] = [
        items[target],
        items[index],
      ];

      return { ...old, exercises: items };
    });
  }

  function add(exercise: Exercise) {
    if (
      form.exercises.length >= 100 ||
      busy ||
      !editing
    ) {
      return;
    }

    const item: ItemForm = {
      mode: '',
      key: crypto.randomUUID(),
      id: null,
      exerciseId: exercise.id,
      name: exercise.name,
      instructions:
        exercise.instructions ?? '',
      sets: '',
      repetitions: '',
      durationSeconds: '',
      frequency: '',
      notes: '',
    };

    setForm((old) => ({
      ...old,
      exercises: [
        ...old.exercises,
        item,
      ],
    }));

    setPicking(false);
    setSuccess('');
  }

  const frozen = busy || archived;

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      {guard.discardDialog}

      <div className="flex flex-wrap gap-3">
        <Button asChild variant="outline">
          <Link
            href={`/patients/${base.patientId}/therapy-plans`}
          >
            Lista planów
          </Link>
        </Button>
        <Button asChild variant="ghost">
          <Link
            href={`/patients/${base.patientId}`}
          >
            Karta pacjenta
          </Link>
        </Button>
      </div>

      <header className="space-y-4 rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-5 sm:p-7">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="text-sm text-muted-foreground">
              {patientName}
            </p>
            <h1 className="mt-2 text-4xl">
              {base.content.name ||
                'Nowy plan terapii'}
            </h1>
          </div>

          <div className="flex flex-wrap gap-2">
            <StatusBadge
              status={base.status}
            />
            <span className="rounded-full border px-3 py-1 text-xs">
              Wersja {base.currentVersion}
            </span>
            {correcting && (
              <span className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
                Korekta
              </span>
            )}
          </div>
        </div>

        <p className="text-xs text-muted-foreground">
          Aktualizacja:{' '}
          {recordDate(
            base.updatedAt ??
              base.createdAt,
          )}
          . Aktywacja nie udostępnia planu
          pacjentowi.
        </p>

        <div className="flex flex-wrap gap-3">
          {base.status === 'Active' &&
            !correcting && (
              <Button
                type="button"
                disabled={
                  frozen ||
                  blocked ||
                  Boolean(remote)
                }
                onClick={() => {
                  setCorrecting(true);
                  setStep(0);
                  setMessage('');
                  setSuccess('');
                }}
              >
                Wprowadź korektę
              </Button>
            )}

          <Button
            type="button"
            variant="outline"
            disabled={busy}
            aria-expanded={history}
            onClick={() =>
              setHistory(
                (value) => !value,
              )
            }
          >
            Historia wersji
          </Button>

          <Button
            type="button"
            variant="ghost"
            disabled={busy}
            onClick={() =>
              void fetchCurrent()
            }
          >
            Pobierz aktualny plan
          </Button>
        </div>
      </header>

      {archived && (
        <p className="rounded-xl border border-amber-200 bg-amber-50/50 p-4 text-sm">
          Karta pacjenta jest archiwalna.
          Wszystkie zmiany są zablokowane.
          Lokalnie wpisana treść nie została
          usunięta.
        </p>
      )}

      {archived && dirty && (
        <details className="rounded-xl border p-5">
          <summary className="cursor-pointer text-sm">
            Lokalny podgląd niezapisanych zmian
          </summary>
          <div className="mt-4">
            <PlanView
              content={preview(form)}
            />
          </div>
        </details>
      )}

      {remote && (
        <section className="space-y-4 rounded-2xl border border-amber-300 p-5">
          <h2 className="text-3xl">
            Aktualna wersja serwera
          </h2>

          <StatusBadge
            status={remote.status}
          />

          <details>
            <summary className="cursor-pointer text-sm font-medium">
              Pokaż pełną treść i porównaj
              z formularzem
            </summary>
            <div className="mt-4">
              <PlanView
                content={remote.content}
              />
            </div>
          </details>

          <label className="flex gap-3 text-sm">
            <input
              type="checkbox"
              checked={compared}
              onChange={(event) =>
                setCompared(
                  event.target.checked,
                )
              }
            />
            Porównałem plan i świadomie
            wybieram lokalną treść jako pełny
            zamiennik. Pozycje usunięte na
            serwerze trzeba usunąć lub dodać
            ponownie w formularzu.
          </label>

          <div className="flex flex-wrap gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() =>
                guard.confirmLeave(() =>
                  accept(false),
                )
              }
            >
              Przyjmij plan serwera
            </Button>

            <Button
              type="button"
              disabled={
                busy ||
                !compared ||
                archived ||
                ![
                  'Draft',
                  'Active',
                ].includes(remote.status)
              }
              onClick={() =>
                accept(true)
              }
            >
              Zachowaj lokalną treść do dalszej edycji
            </Button>
          </div>

          <p className="text-xs text-muted-foreground">
            To nie zapisuje zmian. Aktywny
            plan wymaga następnie korekty
            z uzasadnieniem.
          </p>
        </section>
      )}

      {editing ? (
        <form
          className="space-y-5"
          noValidate
          onSubmit={(event) =>
            event.preventDefault()
          }
        >
          <nav
            aria-label="Etapy planu"
            className="grid gap-2 sm:grid-cols-3"
          >
            {[
              'Dane planu',
              'Ćwiczenia i parametry',
              'Podsumowanie',
            ].map((label, index) => (
              <Button
                key={label}
                type="button"
                variant={
                  step === index
                    ? 'default'
                    : 'outline'
                }
                aria-current={
                  step === index
                    ? 'step'
                    : undefined
                }
                disabled={busy}
                onClick={() =>
                  setStep(index)
                }
              >
                {index + 1}. {label}
              </Button>
            ))}
          </nav>

          <p
            role="status"
            className="text-xs text-muted-foreground"
          >
            {dirty
              ? 'Masz niezapisane zmiany. Zapis jest ręczny.'
              : 'Brak niezapisanych zmian.'}
          </p>

          {step === 0 && (
            <fieldset
              disabled={frozen}
              className="grid gap-5 rounded-2xl border bg-card p-5 sm:grid-cols-2"
            >
              <div className="sm:col-span-2">
                <Field
                  label="Nazwa planu"
                  value={form.name}
                  max={200}
                  error={errors.name}
                  onChange={(name) =>
                    setForm((old) => ({
                      ...old,
                      name,
                    }))
                  }
                />
              </div>

              <Field
                label="Początek terapii"
                value={form.startDate}
                type="date"
                error={errors.startDate}
                onChange={(startDate) =>
                  setForm((old) => ({
                    ...old,
                    startDate,
                  }))
                }
              />

              <Field
                label="Koniec terapii"
                value={form.endDate}
                type="date"
                error={errors.endDate}
                onChange={(endDate) =>
                  setForm((old) => ({
                    ...old,
                    endDate,
                  }))
                }
              />

              <Field
                label="Cel terapii (opcjonalnie)"
                value={form.goal}
                multiline
                max={10000}
                error={errors.goal}
                onChange={(goal) =>
                  setForm((old) => ({
                    ...old,
                    goal,
                  }))
                }
              />

              <Field
                label="Zalecenia ogólne (opcjonalnie)"
                value={
                  form.generalRecommendations
                }
                multiline
                max={10000}
                error={
                  errors.generalRecommendations
                }
                onChange={(
                  generalRecommendations,
                ) =>
                  setForm((old) => ({
                    ...old,
                    generalRecommendations,
                  }))
                }
              />
            </fieldset>
          )}

          {step === 1 && (
            <section className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="text-3xl">
                  Ćwiczenia:{' '}
                  {form.exercises.length}/100
                </h2>

                <Button
                  type="button"
                  disabled={
                    frozen ||
                    form.exercises.length >=
                      100
                  }
                  onClick={() =>
                    setPicking(true)
                  }
                >
                  <Plus
                    aria-hidden="true"
                    className="mr-2 h-4 w-4"
                  />
                  Dodaj z biblioteki
                </Button>
              </div>

              {errors.exercises && (
                <p
                  role="alert"
                  className="text-sm text-destructive"
                >
                  {errors.exercises}
                </p>
              )}

              {!form.exercises.length && (
                <p className="rounded-xl border border-dashed p-6 text-sm">
                  Dodaj pierwsze ćwiczenie
                  z biblioteki.
                </p>
              )}

              {form.exercises.map(
                (item, index) => {
                  const prefix =
                    `exercises[${index}]`;

                  return (
                    <article
                      key={item.key}
                      className="space-y-4 rounded-2xl border bg-card p-5"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <h3 className="text-2xl">
                          {index + 1}.{' '}
                          {item.name ||
                            'Ćwiczenie bez nazwy'}
                        </h3>

                        <div className="flex gap-2">
                          <Button
                            type="button"
                            variant="outline"
                            size="icon"
                            aria-label={`Przesuń ${item.name} w górę`}
                            disabled={
                              frozen ||
                              index === 0
                            }
                            onClick={() =>
                              move(
                                item.key,
                                -1,
                              )
                            }
                          >
                            <ArrowUp
                              aria-hidden="true"
                              className="h-4 w-4"
                            />
                          </Button>

                          <Button
                            type="button"
                            variant="outline"
                            size="icon"
                            aria-label={`Przesuń ${item.name} w dół`}
                            disabled={
                              frozen ||
                              index ===
                                form.exercises
                                  .length -
                                  1
                            }
                            onClick={() =>
                              move(
                                item.key,
                                1,
                              )
                            }
                          >
                            <ArrowDown
                              aria-hidden="true"
                              className="h-4 w-4"
                            />
                          </Button>

                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={`Usuń pozycję ${index + 1}: ${item.name}`}
                            disabled={frozen}
                            onClick={() =>
                              setForm(
                                (old) => ({
                                  ...old,
                                  exercises:
                                    old.exercises.filter(
                                      (
                                        value,
                                      ) =>
                                        value.key !==
                                        item.key,
                                    ),
                                }),
                              )
                            }
                          >
                            <Trash2
                              aria-hidden="true"
                              className="h-4 w-4"
                            />
                          </Button>
                        </div>
                      </div>

                      <details>
                        <summary className="cursor-pointer text-sm text-primary">
                          Instrukcja zapisana
                          w pozycji
                        </summary>
                        <p className="mt-3 whitespace-pre-wrap break-words text-sm leading-7">
                          {item.instructions ||
                            'Brak instrukcji. Zastąp pozycję poprawnym ćwiczeniem.'}
                        </p>
                      </details>

                      {errors[
                        `${prefix}.id`
                      ] && (
                        <p
                          role="alert"
                          className="text-sm text-destructive"
                        >
                          {
                            errors[
                              `${prefix}.id`
                            ]
                          }
                        </p>
                      )}

                      <fieldset
                        disabled={frozen}
                        className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
                      >
                        <Field
                          label="Serie"
                          type="number"
                          value={item.sets}
                          error={
                            errors[
                              `${prefix}.sets`
                            ]
                          }
                          onChange={(sets) =>
                            patchItem(
                              item.key,
                              { sets },
                            )
                          }
                        />

                        <label className="space-y-2 text-sm">
                          <span>
                            Sposób wykonania
                          </span>
                          <select
                            className="h-10 w-full rounded-md border bg-background px-3"
                            value={item.mode}
                            onChange={(
                              event,
                            ) => {
                              const mode =
                                event.target
                                  .value as ItemForm['mode'];

                              patchItem(
                                item.key,
                                mode ===
                                  'time'
                                  ? {
                                      mode,
                                      repetitions:
                                        '',
                                    }
                                  : mode ===
                                      'reps'
                                    ? {
                                        mode,
                                        durationSeconds:
                                          '',
                                      }
                                    : {
                                        mode,
                                        repetitions:
                                          '',
                                        durationSeconds:
                                          '',
                                      },
                              );
                            }}
                          >
                            <option value="">
                              Wybierz
                            </option>
                            <option value="reps">
                              Liczba powtórzeń
                            </option>
                            <option value="time">
                              Czas serii
                              w sekundach
                            </option>
                          </select>
                        </label>

                        {item.mode ===
                        'time' ? (
                          <Field
                            label="Czas jednej serii (sekundy)"
                            type="number"
                            value={
                              item.durationSeconds
                            }
                            error={
                              errors[
                                `${prefix}.durationSeconds`
                              ] ||
                              errors[
                                `${prefix}.repetitions`
                              ]
                            }
                            onChange={(
                              durationSeconds,
                            ) =>
                              patchItem(
                                item.key,
                                {
                                  durationSeconds,
                                  repetitions:
                                    '',
                                  mode: 'time',
                                },
                              )
                            }
                          />
                        ) : (
                          <Field
                            label="Powtórzenia na serię"
                            type="number"
                            value={
                              item.repetitions
                            }
                            error={
                              errors[
                                `${prefix}.repetitions`
                              ]
                            }
                            onChange={(
                              repetitions,
                            ) =>
                              patchItem(
                                item.key,
                                {
                                  repetitions,
                                  durationSeconds:
                                    '',
                                  mode: 'reps',
                                },
                              )
                            }
                          />
                        )}

                        <Field
                          label="Częstotliwość, np. 2 razy dziennie"
                          value={
                            item.frequency
                          }
                          max={200}
                          error={
                            errors[
                              `${prefix}.frequency`
                            ]
                          }
                          onChange={(
                            frequency,
                          ) =>
                            patchItem(
                              item.key,
                              { frequency },
                            )
                          }
                        />

                        <div className="sm:col-span-2">
                          <Field
                            label="Indywidualne uwagi"
                            value={item.notes}
                            max={2000}
                            multiline
                            error={
                              errors[
                                `${prefix}.notes`
                              ]
                            }
                            onChange={(
                              notes,
                            ) =>
                              patchItem(
                                item.key,
                                { notes },
                              )
                            }
                          />
                        </div>
                      </fieldset>
                    </article>
                  );
                },
              )}
            </section>
          )}

          {step === 2 && (
            <>
              <p className="text-sm text-muted-foreground">
                Podgląd formularza przed zapisem.
                Nazwa i instrukcja nowych pozycji
                zostaną ostatecznie skopiowane
                przez backend z biblioteki
                podczas zapisu.
              </p>

              <PlanView
                content={preview(form)}
              />

              {correcting && (
                <Field
                  label="Uzasadnienie korekty *"
                  value={reason}
                  max={1000}
                  multiline
                  disabled={frozen}
                  error={
                    errors.correctionReason
                  }
                  onChange={setReason}
                />
              )}
            </>
          )}

          {message && (
            <p
              role="alert"
              className="text-sm text-destructive"
            >
              {message}
            </p>
          )}

          <div className="flex flex-wrap gap-3">
            {step > 0 && (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() =>
                  setStep(step - 1)
                }
              >
                Wstecz
              </Button>
            )}

            {step < 2 && (
              <Button
                type="button"
                disabled={busy}
                onClick={() =>
                  setStep(step + 1)
                }
              >
                Dalej
              </Button>
            )}

            {base.status === 'Draft' && (
              <Button
                type="button"
                variant={
                  step === 2
                    ? 'default'
                    : 'outline'
                }
                disabled={
                  frozen ||
                  blocked ||
                  Boolean(remote)
                }
                onClick={() =>
                  send('save')
                }
              >
                {busy
                  ? 'Zapisywanie…'
                  : 'Zapisz szkic'}
              </Button>
            )}

            {step === 2 &&
              (correcting ? (
                <Button
                  type="button"
                  disabled={
                    frozen ||
                    blocked ||
                    Boolean(remote)
                  }
                  onClick={() =>
                    request('correct')
                  }
                >
                  Zapisz korektę jako nową wersję
                </Button>
              ) : (
                <Button
                  type="button"
                  disabled={
                    frozen ||
                    dirty ||
                    blocked ||
                    Boolean(remote)
                  }
                  onClick={() =>
                    request('activate')
                  }
                >
                  Aktywuj zapisany szkic
                </Button>
              ))}

            {correcting && (
              <Button
                type="button"
                variant="ghost"
                disabled={busy}
                onClick={() =>
                  guard.confirmLeave(() => {
                    setForm(
                      planForm(
                        base.content,
                      ),
                    );
                    setReason('');
                    setCorrecting(false);
                    setErrors({});
                    setMessage('');
                  })
                }
              >
                Anuluj korektę
              </Button>
            )}
          </div>

          {dirty &&
            base.status === 'Draft' && (
              <p className="text-xs text-muted-foreground">
                Przed aktywacją zapisz szkic.
                Wymagane są nazwa, obie daty
                i kompletne parametry ćwiczeń.
              </p>
            )}
        </form>
      ) : (
        <PlanView content={base.content} />
      )}

      {!editing && message && (
        <p
          role="alert"
          className="text-sm text-destructive"
        >
          {message}
        </p>
      )}

      {success && (
        <p
          role="status"
          className="text-sm text-primary"
        >
          {success}
        </p>
      )}

      {!archived && (
        <div className="flex flex-wrap gap-3 border-t pt-5">
          {base.status === 'Active' && (
            <Button
              type="button"
              variant="outline"
              disabled={
                busy ||
                dirty ||
                blocked ||
                Boolean(remote)
              }
              onClick={() =>
                request('complete')
              }
            >
              Zakończ plan
            </Button>
          )}

          {base.status !== 'Archived' && (
            <Button
              type="button"
              variant="outline"
              disabled={
                busy ||
                dirty ||
                blocked ||
                Boolean(remote)
              }
              onClick={() =>
                request('archive')
              }
            >
              Archiwizuj plan
            </Button>
          )}
        </div>
      )}

      {history && (
        <PlanHistory
          accountId={accountId}
          planId={base.id}
        />
      )}

      {picking && (
        <Dialog
          open
          onOpenChange={(open) => {
            if (!busy) setPicking(open);
          }}
        >
          <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-5xl overflow-y-auto rounded-2xl">
            <DialogHeader>
              <DialogTitle>
                Dodaj ćwiczenie do planu
              </DialogTitle>
              <DialogDescription>
                Wybieraj z bieżącej biblioteki.
                To samo ćwiczenie można dodać
                kilka razy z różnymi parametrami.
              </DialogDescription>
            </DialogHeader>

            <ExerciseLibrary
              onPick={add}
              disabled={
                busy ||
                form.exercises.length >=
                  100
              }
            />
          </DialogContent>
        </Dialog>
      )}

      {confirmation && (
        <Confirm
          title={
            actionTitles[confirmation]
          }
          text={
            actionTexts[confirmation]
          }
          busy={busy}
          onClose={() =>
            setConfirmation(null)
          }
          onConfirm={() =>
            send(confirmation)
          }
        />
      )}
    </div>
  );
}

function PlanHistory({
  accountId,
  planId,
}: {
  accountId: string;
  planId: string;
}) {
  const [selected, setSelected] =
    useState<number | null>(null);

  const query = useQuery({
    queryKey: [
      'therapy-plan-history',
      accountId,
      planId,
    ],
    queryFn: ({ signal }) =>
      therapyApi.versions(
        planId,
        signal,
      ),
    ...therapyOptions,
  });

  if (query.isError) {
    return (
      <TherapyError
        error={query.error}
        retry={() => void query.refetch()}
      />
    );
  }

  if (query.isPending) {
    return (
      <LoadingState title="Pobieranie historii planu…" />
    );
  }

  const version = query.data.find(
    (item) =>
      item.versionNumber === selected,
  );

  return (
    <section className="space-y-4 border-t pt-6">
      <h2 className="text-3xl">
        Historia aktywowanych wersji
      </h2>

      <p className="text-sm text-muted-foreground">
        Zapisy szkicu oraz zakończenie
        i archiwizacja nie tworzą nowych
        wersji treści.
      </p>

      {!query.data.length && (
        <p>
          Plan nie ma jeszcze aktywowanych
          wersji.
        </p>
      )}

      <div className="flex flex-wrap gap-3">
        {query.data.map((item) => (
          <Button
            key={item.versionNumber}
            type="button"
            variant={
              selected ===
              item.versionNumber
                ? 'default'
                : 'outline'
            }
            aria-pressed={
              selected ===
              item.versionNumber
            }
            onClick={() =>
              setSelected(
                item.versionNumber,
              )
            }
          >
            Wersja {item.versionNumber}
          </Button>
        ))}
      </div>

      {version && (
        <article className="space-y-4">
          <p className="text-sm">
            Wersja{' '}
            {version.versionNumber}
            {' · '}
            {recordDate(
              version.activatedAt,
            )}
          </p>

          <p className="break-all text-xs text-muted-foreground">
            Autor:{' '}
            {version.authorUserId ===
            accountId
              ? 'Twoje konto'
              : version.authorUserId}
          </p>

          <p className="whitespace-pre-wrap break-words text-sm">
            {version.correctionReason ||
              'Pierwsza aktywacja.'}
          </p>

          <PlanView
            content={version.content}
          />
        </article>
      )}
    </section>
  );
}