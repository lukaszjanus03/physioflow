'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Archive,
  ArrowUpRight,
  BookOpen,
  ChevronDown,
  Dumbbell,
  Info,
  Plus,
  RefreshCw,
  Search,
  SlidersHorizontal,
  X,
} from 'lucide-react';
import { Manrope } from 'next/font/google';
import { useRef, useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { LoadingState } from '@/components/ui/loading-state';
import { Input } from '@/components/ui/primitives';
import { Confirm, Field, Pages, TherapyError } from '@/components/therapy/TherapyUI';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { exerciseErrors } from '@/lib/therapy-form';
import {
  therapyApi,
  therapyError,
  therapyFieldErrors,
  therapyOptions,
  therapyStatus,
} from '@/lib/therapy-api';
import type { Exercise, ExerciseBody } from '@/lib/therapy-api';
import { useAuthStore } from '@/stores/auth-store';

import styles from './ExerciseLibrary.module.css';

const libraryFont = Manrope({
  subsets: ['latin', 'latin-ext'],
  display: 'swap',
});

const EMPTY_SEARCH = {
  search: '',
  category: '',
  archived: 'false',
};

const INITIAL_FILTERS = {
  ...EMPTY_SEARCH,
  page: 1,
};

export default function ExerciseLibrary({
  onPick,
  disabled = false,
}: {
  onPick?: (exercise: Exercise) => void;
  disabled?: boolean;
}) {
  const accountId = useAuthStore((state) => state.userId);
  const [filters, setFilters] = useState(INITIAL_FILTERS);
  const [searchForm, setSearchForm] = useState(EMPTY_SEARCH);
  const [selection, setSelection] = useState<string | null>(null);

  const hasFilters = Boolean(
    filters.search || filters.category || filters.archived !== 'false',
  );

  const query = useQuery({
    queryKey: ['therapy-exercises', accountId, filters],
    queryFn: ({ signal }) => therapyApi.exercises(filters, signal),
    enabled: Boolean(accountId),
    ...therapyOptions,
  });

  function clearFilters() {
    setSearchForm(EMPTY_SEARCH);
    setFilters(INITIAL_FILTERS);
  }

  return (
    <section className={`${libraryFont.className} ${styles.library}`}>
      <header className={styles.hero}>
        <div className={styles.heroContent}>
          <div className={styles.eyebrow}>
            <span className={styles.heroIcon}>
              <Dumbbell aria-hidden="true" size={19} />
            </span>
            Materiały terapeutyczne
          </div>

          <h1 className={styles.title}>
            {onPick ? 'Wybierz ćwiczenie' : 'Biblioteka ćwiczeń'}
          </h1>

          <p className={styles.intro}>
            {onPick
              ? 'Wybierz instrukcję z biblioteki. Parametry wykonania ustalisz w planie.'
              : 'Twoje instrukcje. Twoje metody. Baza ćwiczeń do indywidualnych planów terapii.'}
          </p>
        </div>

        {!onPick && (
          <Button
            type="button"
            className={styles.mainAction}
            onClick={() => setSelection('new')}
          >
            <Plus aria-hidden="true" size={17} />
            Dodaj ćwiczenie
          </Button>
        )}
      </header>

      <form
        className={`${styles.filters} ${onPick ? styles.pickerFilters : ''}`}
        role="search"
        aria-label="Wyszukaj w bibliotece ćwiczeń"
        onSubmit={(event) => {
          event.preventDefault();

          setFilters({
            search: searchForm.search.trim(),
            category: searchForm.category.trim(),
            archived: onPick ? 'false' : searchForm.archived,
            page: 1,
          });
        }}
      >
        <label className={styles.filterLabel}>
          Nazwa lub instrukcja

          <span className={styles.searchBox}>
            <Search aria-hidden="true" size={17} />

            <Input
              name="search"
              type="search"
              className={styles.textInput}
              placeholder="Znajdź ćwiczenie…"
              maxLength={200}
              value={searchForm.search}
              onChange={(event) =>
                setSearchForm((old) => ({
                  ...old,
                  search: event.target.value,
                }))
              }
            />
          </span>
        </label>

        <label className={styles.filterLabel}>
          Pełna nazwa kategorii

          <Input
            name="category"
            className={styles.textInput}
            placeholder="Np. Obręcz barkowa"
            maxLength={100}
            value={searchForm.category}
            onChange={(event) =>
              setSearchForm((old) => ({
                ...old,
                category: event.target.value,
              }))
            }
          />
        </label>

        {!onPick && (
          <label className={styles.filterLabel}>
            Widok

            <select
              name="archived"
              className={styles.select}
              value={searchForm.archived}
              onChange={(event) =>
                setSearchForm((old) => ({
                  ...old,
                  archived: event.target.value,
                }))
              }
            >
              <option value="false">Bieżące</option>
              <option value="true">Archiwalne</option>
              <option value="">Wszystkie</option>
            </select>
          </label>
        )}

        <div className={styles.filterActions}>
          <Button
            type="submit"
            variant="secondary"
            disabled={query.isFetching}
          >
            <SlidersHorizontal aria-hidden="true" size={15} />
            Zastosuj
          </Button>

          <Button
            type="button"
            variant="ghost"
            onClick={clearFilters}
          >
            <X aria-hidden="true" size={15} />
            Wyczyść
          </Button>
        </div>
      </form>

      <div className={styles.resultsHeader}>
        <div className={styles.resultsTitle}>
          <h2 className={styles.sectionTitle}>
            {hasFilters ? 'Wyniki wyszukiwania' : 'Twoje ćwiczenia'}
          </h2>

          <span
            className={styles.resultCount}
            aria-live="polite"
            aria-atomic="true"
          >
            {query.isError ? '—' : (query.data?.totalCount ?? '…')}
          </span>
        </div>

        <Button
          type="button"
          variant="ghost"
          size="sm"
          className={styles.refresh}
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          <RefreshCw
            aria-hidden="true"
            size={15}
            className={
              query.isFetching
                ? 'animate-spin motion-reduce:animate-none'
                : ''
            }
          />
          Odśwież
        </Button>
      </div>

      <div aria-busy={query.isFetching}>
        {query.isError ? (
          <TherapyError
            error={query.error}
            retry={() => void query.refetch()}
          />
        ) : query.isPending ? (
          <LoadingState title="Pobieranie ćwiczeń…" />
        ) : (
          <>
            {!query.data.items.length && (
              <div className={styles.empty}>
                <span className={styles.emptyIcon}>
                  <BookOpen aria-hidden="true" size={27} />
                </span>

                <h3>
                  {filters.page > 1
                    ? 'Na tej stronie nie ma ćwiczeń'
                    : hasFilters
                      ? 'Brak pasujących ćwiczeń'
                      : 'Miejsce na Twoje ćwiczenia'}
                </h3>

                <p>
                  {filters.page > 1
                    ? 'Wróć do poprzedniej strony wyników.'
                    : hasFilters
                      ? 'Zmień kryteria wyszukiwania lub wyczyść filtry.'
                      : onPick
                        ? 'Najpierw dodaj instrukcje na stronie biblioteki ćwiczeń.'
                        : 'Dodaj pierwszą instrukcję, aby wykorzystać ją w planie terapii.'}
                </p>

                {filters.page === 1 &&
                  (hasFilters ? (
                    <Button
                      type="button"
                      variant="outline"
                      onClick={clearFilters}
                    >
                      Wyczyść filtry
                    </Button>
                  ) : !onPick ? (
                    <Button
                      type="button"
                      onClick={() => setSelection('new')}
                    >
                      <Plus aria-hidden="true" size={16} />
                      Dodaj pierwsze ćwiczenie
                    </Button>
                  ) : null)}
              </div>
            )}

            <div className={styles.cards}>
              {query.data.items.map((exercise) => (
                <article
                  key={exercise.id}
                  className={styles.card}
                >
                  <div className={styles.cardBody}>
                    <div className={styles.cardMeta}>
                      <span className={styles.category}>
                        {exercise.category || 'Bez kategorii'}
                      </span>

                      {exercise.isArchived ? (
                        <span className={styles.archived}>
                          <Archive aria-hidden="true" size={13} />
                          Archiwalne
                        </span>
                      ) : (
                        <Dumbbell
                          aria-hidden="true"
                          size={21}
                          className={styles.cardIcon}
                        />
                      )}
                    </div>

                    <h2 className={styles.cardTitle}>
                      {exercise.name}
                    </h2>

                    <p className={styles.preview}>
                      {instructionPreview(exercise.instructions)}
                    </p>

                    <details className={styles.instructions}>
                      <summary>
                        <BookOpen aria-hidden="true" size={15} />
                        Pełna instrukcja i uwagi
                        <ChevronDown aria-hidden="true" size={15} />
                      </summary>

                      <ExerciseView exercise={exercise} />
                    </details>
                  </div>

                  <div className={styles.cardFooter}>
                    {onPick ? (
                      <Button
                        type="button"
                        className={styles.cardAction}
                        disabled={disabled || exercise.isArchived}
                        onClick={() => onPick(exercise)}
                      >
                        <Plus aria-hidden="true" size={16} />
                        Dodaj do planu
                      </Button>
                    ) : (
                      <Button
                        type="button"
                        variant="outline"
                        className={styles.cardAction}
                        onClick={() => setSelection(exercise.id)}
                      >
                        Szczegóły i edycja
                        <ArrowUpRight aria-hidden="true" size={16} />
                      </Button>
                    )}
                  </div>
                </article>
              ))}
            </div>

            {(query.data.totalPages > 1 || filters.page > 1) && (
              <div className={styles.pagination}>
                <Pages
                  page={filters.page}
                  pages={query.data.totalPages}
                  next={query.data.hasNext}
                  busy={query.isFetching}
                  onChange={(page) =>
                    setFilters((old) => ({
                      ...old,
                      page,
                    }))
                  }
                />
              </div>
            )}
          </>
        )}
      </div>

      <details className={styles.hint}>
        <summary>
          <Info aria-hidden="true" size={15} />
          Jak działają kopie ćwiczeń w planach?
          <ChevronDown aria-hidden="true" size={14} />
        </summary>

        <p>
          Dodanie ćwiczenia do planu zapisuje kopię jego nazwy i instrukcji.
          Późniejsza edycja lub archiwizacja w bibliotece nie zmienia wcześniej
          zapisanych planów.
        </p>
      </details>

      {selection && (
        <ExerciseLoader
          key={`${accountId}:${selection}`}
          accountId={accountId ?? ''}
          exerciseId={selection}
          onClose={() => setSelection(null)}
        />
      )}
    </section>
  );
}

function instructionPreview(instructions: string | null) {
  const text = instructions?.replace(/\s+/g, ' ').trim();

  if (!text) return 'Brak instrukcji wykonania.';

  return text.length > 190
    ? `${text.slice(0, 190).trimEnd()}…`
    : text;
}

function ExerciseView({
  exercise,
}: {
  exercise: Exercise | ExerciseBody;
}) {
  return (
    <div className={styles.fullInstructions}>
      <p>{exercise.instructions || 'Brak instrukcji.'}</p>

      {exercise.notes && (
        <div className={styles.notes}>
          <span>Wewnętrzne uwagi</span>
          <p>{exercise.notes}</p>
        </div>
      )}
    </div>
  );
}

function ExerciseLoader({
  exerciseId,
  accountId,
  onClose,
}: {
  exerciseId: string;
  accountId: string;
  onClose: () => void;
}) {
  const query = useQuery({
    queryKey: ['therapy-exercise', accountId, exerciseId],
    queryFn: ({ signal }) => therapyApi.exercise(exerciseId, signal),
    enabled: exerciseId !== 'new',
    ...therapyOptions,
  });

  if (exerciseId === 'new') {
    return (
      <ExerciseEditor
        initial={null}
        accountId={accountId}
        onClose={onClose}
      />
    );
  }

  if (query.isError) {
    return (
      <div className="space-y-3">
        <TherapyError
          error={query.error}
          retry={() => void query.refetch()}
        />

        <Button
          type="button"
          variant="outline"
          onClick={onClose}
        >
          Zamknij edytor
        </Button>
      </div>
    );
  }

  if (query.isPending) {
    return <LoadingState title="Pobieranie ćwiczenia…" />;
  }

  return (
    <ExerciseEditor
      initial={query.data}
      accountId={accountId}
      onClose={onClose}
    />
  );
}

function exerciseForm(exercise: Exercise | null): ExerciseBody {
  return {
    name: exercise?.name ?? '',
    instructions: exercise?.instructions ?? '',
    category: exercise?.category ?? '',
    notes: exercise?.notes ?? '',
  };
}

function ExerciseEditor({
  initial,
  accountId,
  onClose,
}: {
  initial: Exercise | null;
  accountId: string;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const [base, setBase] = useState(initial);
  const [form, setForm] = useState(() => exerciseForm(initial));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState('');
  const [remote, setRemote] = useState<Exercise | null>(null);
  const [blocked, setBlocked] = useState(false);
  const [reading, setReading] = useState(false);
  const [compared, setCompared] = useState(false);
  const [confirm, setConfirm] = useState(false);

  const lock = useRef(false);

  const dirty =
    JSON.stringify(form) !== JSON.stringify(exerciseForm(base));

  async function readLatest() {
    if (!base) return;

    setReading(true);

    try {
      setRemote(await therapyApi.exercise(base.id));
      setCompared(false);
    } catch (error) {
      setMessage(therapyError(error));
    } finally {
      setReading(false);
    }
  }

  const mutation = useMutation({
    mutationFn: (archive: boolean) =>
      archive && base
        ? therapyApi.archiveExercise(base)
        : therapyApi.saveExercise(base, form),
    retry: false,
    onSuccess: async () => {
      await client.invalidateQueries({
        queryKey: ['therapy-exercises', accountId],
      });

      toast.success('Ćwiczenie zapisane.');
      onClose();
    },
    onError: async (error) => {
      setMessage(therapyError(error));
      setErrors(therapyFieldErrors(error));
      setConfirm(false);

      if (therapyStatus(error) === 409) {
        setBlocked(true);
        await readLatest();
      }
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  const busy = mutation.isPending || reading;
  const guard = useUnsavedChanges(dirty, busy);

  function send(archive: boolean) {
    if (lock.current || busy || blocked || remote) {
      return;
    }

    const next = archive ? {} : exerciseErrors(form);

    setErrors(next);

    if (Object.keys(next).length) {
      setMessage('Sprawdź pola formularza.');
      return;
    }

    if (archive && dirty) {
      setMessage('Najpierw zapisz albo odrzuć zmiany.');
      return;
    }

    lock.current = true;
    setMessage('');
    mutation.mutate(archive);
  }

  function accept(keep: boolean) {
    if (!remote || (keep && !compared)) {
      return;
    }

    if (!keep) {
      setForm(exerciseForm(remote));
    }

    setBase(remote);
    setRemote(null);
    setBlocked(false);
    setErrors({});
    setMessage('');
  }

  return (
    <>
      <Dialog
        open
        onOpenChange={(open) => {
          if (!open) {
            guard.confirmLeave(onClose);
          }
        }}
      >
        <DialogContent
          className={`${libraryFont.className} ${styles.dialog}`}
        >
          <DialogHeader className={styles.editorHeader}>
            <DialogTitle className={styles.editorTitle}>
              {base ? 'Ćwiczenie w bibliotece' : 'Nowe ćwiczenie'}
            </DialogTitle>

            <DialogDescription>
              Instrukcja jest prywatną częścią biblioteki terapeuty.
              Jej zmiana nie aktualizuje istniejących planów.
            </DialogDescription>
          </DialogHeader>

          {remote && (
            <section className="space-y-3 rounded-xl border border-amber-300 p-4">
              <h3 className="text-2xl">
                Aktualna wersja serwera
              </h3>

              <p>
                {remote.name} ·{' '}
                {remote.isArchived ? 'Archiwalne' : 'Bieżące'}
              </p>

              <ExerciseView exercise={remote} />

              <label className="flex gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={compared}
                  onChange={(event) =>
                    setCompared(event.target.checked)
                  }
                />

                Porównałem treść i świadomie wybieram lokalny formularz
                jako pełny zamiennik.
              </label>

              <div className="flex flex-wrap gap-3">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() =>
                    guard.confirmLeave(() => accept(false))
                  }
                >
                  Przyjmij dane serwera
                </Button>

                <Button
                  type="button"
                  disabled={!compared}
                  onClick={() => accept(true)}
                >
                  Zachowaj lokalne zmiany
                </Button>
              </div>

              <p className="text-xs text-muted-foreground">
                Wybór nie zapisuje automatycznie.
              </p>
            </section>
          )}

          <form
            className="space-y-4"
            noValidate
            onSubmit={(event) => {
              event.preventDefault();
              send(false);
            }}
          >
            <fieldset
              disabled={busy}
              className={styles.editorFields}
            >
              <Field
                label="Nazwa *"
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

              <Field
                label="Kategoria / obszar ciała *"
                value={form.category}
                max={100}
                error={errors.category}
                onChange={(category) =>
                  setForm((old) => ({
                    ...old,
                    category,
                  }))
                }
              />

              <Field
                label="Instrukcja wykonania *"
                value={form.instructions}
                max={10000}
                multiline
                error={errors.instructions}
                onChange={(instructions) =>
                  setForm((old) => ({
                    ...old,
                    instructions,
                  }))
                }
              />

              <Field
                label="Wewnętrzne uwagi do ćwiczenia"
                value={form.notes}
                max={2000}
                multiline
                error={errors.notes}
                onChange={(notes) =>
                  setForm((old) => ({
                    ...old,
                    notes,
                  }))
                }
              />
            </fieldset>

            {message && (
              <p
                role="alert"
                className="text-sm text-destructive"
              >
                {message}
              </p>
            )}

            <div className={styles.editorActions}>
              <Button
                type="submit"
                disabled={busy || blocked || Boolean(remote)}
              >
                {busy ? 'Zapisywanie…' : 'Zapisz ćwiczenie'}
              </Button>

              {base && (
                <Button
                  type="button"
                  variant="outline"
                  disabled={
                    busy || dirty || blocked || Boolean(remote)
                  }
                  onClick={() => setConfirm(true)}
                >
                  {base.isArchived
                    ? 'Przywróć ćwiczenie'
                    : 'Archiwizuj ćwiczenie'}
                </Button>
              )}

              <Button
                type="button"
                variant="ghost"
                disabled={busy}
                onClick={() => guard.confirmLeave(onClose)}
              >
                Zamknij
              </Button>
            </div>
          </form>

          {blocked && !remote && (
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() => void readLatest()}
            >
              Pobierz aktualną wersję
            </Button>
          )}
        </DialogContent>
      </Dialog>

      {guard.discardDialog}

      {confirm && (
        <Confirm
          title={
            base?.isArchived
              ? 'Przywrócić ćwiczenie?'
              : 'Archiwizować ćwiczenie?'
          }
          text="Istniejące plany zachowają własne kopie instrukcji. Ćwiczenia archiwalne nie są dostępne do dodawania jako nowe pozycje."
          busy={busy}
          onClose={() => setConfirm(false)}
          onConfirm={() => send(true)}
        />
      )}
    </>
  );
}