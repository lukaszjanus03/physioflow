'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useRef, useState } from 'react';
import Link from 'next/link';

import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import { useMarketplaceSession } from '@/hooks/marketplace-hooks';
import { useCurrentTime } from '@/hooks/use-current-time';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { MarketplaceError, publicQueryOptions } from '@/lib/marketplace-api';
import {
  refreshReviews,
  reviewDate,
  reviewError,
  reviewLabels,
  reviewRetryAt,
  reviewStatus,
  reviewsApi,
} from '@/lib/reviews-api';
import type { Eligibility } from '@/lib/reviews-api';
import { Star } from 'lucide-react';

export function RatingSummary({
  averageRating,
  reviewCount,
}: {
  averageRating: number | null;
  reviewCount: number;
}) {
  return (
    <p className="text-sm font-medium text-primary">
      {reviewCount > 0 && averageRating !== null
        ? `${averageRating.toLocaleString('pl-PL', {
            minimumFractionDigits: 1,
            maximumFractionDigits: 1,
          })}/5 · Liczba opinii: ${reviewCount}`
        : 'Brak opinii'}
    </p>
  );
}

export function ReviewFailure({ error, retry }: { error: unknown; retry?: () => void }) {
  const now = useCurrentTime();

  const wait = error instanceof MarketplaceError ? Math.max(0, Math.ceil((error.retryAt - now) / 1000)) : 0;

  return (
    <div role="alert" className="space-y-3 rounded-xl border border-destructive/30 p-4 text-sm">
      <p className="text-destructive">
        {error instanceof MarketplaceError
          ? error.status === 404
            ? 'Publiczne opinie tego profilu są niedostępne.'
            : error.message
          : reviewError(error)}
      </p>

      {retry && (
        <Button type="button" variant="outline" disabled={wait > 0} onClick={retry}>
          {wait > 0 ? `Poczekaj ${wait} s` : 'Pobierz aktualne dane'}
        </Button>
      )}
    </div>
  );
}

export function ReviewPages({
  page,
  pages,
  next,
  busy,
  onChange,
}: {
  page: number;
  pages: number;
  next: boolean;
  busy: boolean;
  onChange: (page: number) => void;
}) {
  return (
    <nav aria-label="Strony opinii" className="flex flex-wrap items-center justify-between gap-3">
      <Button type="button" variant="outline" disabled={page <= 1 || busy} onClick={() => onChange(page - 1)}>
        Poprzednia
      </Button>

      <span className="text-sm">
        Strona {page} z {Math.max(1, pages)}
      </span>

      <Button type="button" variant="outline" disabled={!next || busy} onClick={() => onChange(page + 1)}>
        Następna
      </Button>
    </nav>
  );
}

export default function TherapistReviews({ therapistId }: { therapistId: string }) {
  const session = useMarketplaceSession();
  const [page, setPage] = useState(1);

  const valid = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(therapistId);

  const query = useQuery({
    queryKey: ['therapist-reviews', therapistId, page],
    queryFn: ({ signal }) => reviewsApi.public(therapistId, page, signal),
    enabled: valid,
    ...publicQueryOptions,
    refetchInterval: 60_000,
  });

  if (!valid) {
    return null;
  }

  return (
    <section id="opinie" className="space-y-6 rounded-2xl border bg-card p-5 sm:p-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-3xl">Opinie pacjentów</h2>

        <Button
          type="button"
          variant="outline"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          Odśwież
        </Button>
      </div>

      {query.isError ? (
        <ReviewFailure error={query.error} retry={() => void query.refetch()} />
      ) : query.isPending ? (
        <LoadingState title="Wczytywanie opinii…" />
      ) : (
        <>
          <RatingSummary averageRating={query.data.averageRating} reviewCount={query.data.reviewCount} />

          {query.data.reviews.items.length === 0 && (
            <p className="text-sm text-muted-foreground">
              {page > 1
                ? 'Ta strona nie zawiera opinii. Wróć do poprzedniej strony.'
                : 'Nie ma jeszcze opublikowanych opinii.'}
            </p>
          )}

          <div className="space-y-4">
            {query.data.reviews.items.map((review, index) => (
              <article key={`${page}:${index}`} className="space-y-3 rounded-xl border p-5">
                <div className="flex flex-wrap justify-between gap-3">
                  <p className="font-semibold">
                    {[review.firstName, review.lastNameInitial].filter(Boolean).join(' ') || 'Pacjent'}
                  </p>

                  <span className="text-sm text-muted-foreground">{reviewDate(review.date)}</span>
                </div>

                <p className="font-medium text-primary">Ocena: {review.rating}/5</p>

                {review.comment && (
                  <p className="whitespace-pre-wrap break-words text-sm leading-relaxed">{review.comment}</p>
                )}
              </article>
            ))}
          </div>

          {(query.data.reviews.totalPages > 1 || page > 1) && (
            <ReviewPages
              page={page}
              pages={query.data.reviews.totalPages}
              next={query.data.reviews.hasNext}
              busy={query.isFetching}
              onChange={setPage}
            />
          )}
        </>
      )}

      {session.patientId && (
        <OwnReview
          key={`${session.patientId}:${therapistId}`}
          therapistId={therapistId}
          accountId={session.patientId}
        />
      )}

      {session.ready && !session.loggedIn && (
        <p className="text-sm text-muted-foreground">
          Aby wystawić opinię po odbytej wizycie,{' '}
          <Link
            className="text-primary underline"
            href={`/login?${new URLSearchParams({
              next: `/terapeuci/${therapistId}`,
            })}`}
          >
            zaloguj się na konto pacjenta
          </Link>
          .
        </p>
      )}
    </section>
  );
}

function OwnReview({ therapistId, accountId }: { therapistId: string; accountId: string }) {
  const query = useQuery({
    queryKey: ['own-therapist-review', accountId, therapistId],
    queryFn: ({ signal }) => reviewsApi.own(therapistId, signal),
    ...publicQueryOptions,
  });

  return (
    <section className="space-y-4 border-t pt-6">
      <h3 className="text-2xl">Twoja opinia</h3>

      {query.isError ? (
        <ReviewFailure error={query.error} retry={() => void query.refetch()} />
      ) : query.isPending ? (
        <LoadingState title="Sprawdzanie możliwości wystawienia opinii…" />
      ) : (
        <ReviewEditor key={query.data.review?.version ?? 'new'} data={query.data} therapistId={therapistId} />
      )}
    </section>
  );
}

function ReviewEditor({ data, therapistId }: { data: Eligibility; therapistId: string }) {
  const client = useQueryClient();
  const review = data.review;
  const id = useId();

  const [editing, setEditing] = useState(!review);
  const [rating, setRating] = useState(review?.rating ?? 0);
  const [comment, setComment] = useState(review?.comment ?? '');
  const [message, setMessage] = useState('');
  const [retryAt, setRetryAt] = useState(0);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const lock = useRef(false);
  const now = useCurrentTime();

  const wait = Math.max(0, Math.ceil((retryAt - now) / 1000));

  const mutation = useMutation({
    mutationFn: (remove: boolean) =>
      remove
        ? reviewsApi.remove(therapistId)
        : reviewsApi.save(therapistId, Boolean(review), {
            rating,
            comment: comment.trim() || null,
          }),
    retry: false,
    onSuccess: async () => {
      setMessage('');
      setEditing(false);
      setConfirmDelete(false);
      await refreshReviews(client);
    },
    onError: (error) => {
      setMessage(reviewError(error));
      setRetryAt(reviewRetryAt(error, 900));
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  const dirty = editing && (rating !== (review?.rating ?? 0) || comment !== (review?.comment ?? ''));

  const guard = useUnsavedChanges(dirty, mutation.isPending);

  function send(remove: boolean) {
    if (lock.current || mutation.isPending || wait > 0) {
      return;
    }

    if (!remove && (!Number.isInteger(rating) || rating < 1 || rating > 5 || comment.length > 1000)) {
      setMessage('Wybierz ocenę 1–5 i komentarz do 1000 znaków.');
      return;
    }

    lock.current = true;
    setMessage('');
    mutation.mutate(remove);
  }

  if (!review && !data.canCreate) {
    return (
      <p className="text-sm text-muted-foreground">
        Opinię możesz wystawić po odbytej wizycie u tego terapeuty.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      {guard.discardDialog}

      {review && (
        <div className="space-y-3 rounded-xl bg-muted/40 p-4">
          <p className="font-medium">
            {reviewLabels[review.status]} · Ocena: {review.rating}/5
          </p>

          {review.comment && <p className="whitespace-pre-wrap break-words text-sm">{review.comment}</p>}

          {review.rejectionReason && (
            <p className="whitespace-pre-wrap break-words text-sm text-destructive">
              Uzasadnienie: {review.rejectionReason}
            </p>
          )}

          {!editing && (
            <Button
              type="button"
              variant="outline"
              disabled={mutation.isPending}
              onClick={() => setEditing(true)}
            >
              Edytuj opinię
            </Button>
          )}
        </div>
      )}

      {editing && (
        <form
          className="space-y-4"
          aria-busy={mutation.isPending}
          onSubmit={(event) => {
            event.preventDefault();
            send(false);
          }}
        >
          <fieldset disabled={mutation.isPending} className="space-y-3">
            <legend className="mb-3 text-sm font-medium">Twoja ocena</legend>

            <div className="flex flex-wrap items-center gap-4">
              <div className="flex items-center gap-1 sm:gap-2">
                {[1, 2, 3, 4, 5].map((value) => (
                  <label
                    key={value}
                    className={mutation.isPending ? 'cursor-not-allowed opacity-50' : 'cursor-pointer'}
                  >
                    <input
                      type="radio"
                      name={`${id}-rating`}
                      value={value}
                      checked={rating === value}
                      onChange={() => setRating(value)}
                      required
                      className="peer sr-only"
                    />

                    <span
                      className={`flex h-12 w-12 items-center justify-center rounded-xl transition-colors peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-primary ${
                        value <= rating ? 'bg-amber-50 text-amber-500' : 'text-slate-400'
                      } ${mutation.isPending ? '' : 'hover:bg-amber-50 hover:text-amber-500'}`}
                    >
                      <Star
                        aria-hidden="true"
                        className={`h-8 w-8 ${value <= rating ? 'fill-current' : ''}`}
                        strokeWidth={1.5}
                      />

                      <span className="sr-only">Ocena {value} z 5</span>
                    </span>
                  </label>
                ))}
              </div>

              <span role="status" className="text-sm font-medium text-muted-foreground">
                {rating === 0
                  ? 'Wybierz ocenę'
                  : `${rating}/5 · ${
                      ['', 'Bardzo źle', 'Źle', 'Przeciętnie', 'Dobrze', 'Bardzo dobrze'][rating]
                    }`}
              </span>
            </div>
          </fieldset>

          <div className="space-y-2">
            <label htmlFor={`${id}-comment`} className="text-sm font-medium">
              Komentarz (opcjonalnie)
            </label>

            <Textarea
              id={`${id}-comment`}
              value={comment}
              maxLength={1000}
              disabled={mutation.isPending}
              onChange={(event) => setComment(event.target.value)}
              aria-describedby={`${id}-help`}
            />

            <p id={`${id}-help`} className="text-xs text-muted-foreground">
              {comment.length}/1000. Nie wpisuj danych kontaktowych, PESEL-u ani szczegółów dokumentacji
              medycznej.
            </p>
          </div>

          <p className="text-sm text-muted-foreground">
            Nowa lub zmieniona opinia wymaga zatwierdzenia. Edycja opublikowanej opinii wycofa ją z publikacji
            do czasu kolejnej decyzji.
          </p>

          <div className="flex flex-wrap gap-3">
            <Button disabled={mutation.isPending || wait > 0} type="submit">
              {mutation.isPending ? 'Zapisywanie…' : 'Prześlij do moderacji'}
            </Button>

            {review && (
              <Button
                variant="outline"
                type="button"
                disabled={mutation.isPending}
                onClick={() =>
                  guard.confirmLeave(() => {
                    setRating(review.rating);
                    setComment(review.comment ?? '');
                    setEditing(false);
                  })
                }
              >
                Anuluj edycję
              </Button>
            )}
          </div>
        </form>
      )}

      {review && (
        <div className="space-y-3">
          {confirmDelete ? (
            <div className="space-y-3 rounded-xl border border-destructive/30 p-4">
              <p>Czy na pewno usunąć swoją opinię?</p>

              <div className="flex flex-wrap gap-3">
                <Button
                  type="button"
                  variant="destructive"
                  disabled={mutation.isPending || wait > 0}
                  onClick={() => send(true)}
                >
                  Potwierdź usunięcie
                </Button>

                <Button
                  type="button"
                  variant="outline"
                  disabled={mutation.isPending}
                  onClick={() => setConfirmDelete(false)}
                >
                  Zachowaj opinię
                </Button>
              </div>
            </div>
          ) : (
            <Button
              type="button"
              variant="ghost"
              disabled={mutation.isPending}
              onClick={() => guard.confirmLeave(() => setConfirmDelete(true))}
            >
              Usuń opinię
            </Button>
          )}
        </div>
      )}

      {message && (
        <p role="alert" className="text-sm text-destructive">
          {message}
        </p>
      )}

      {wait > 0 && (
        <p role="status" className="text-sm">
          Ponowna próba za {wait} s.
        </p>
      )}

      {mutation.isError && [404, 409].includes(reviewStatus(mutation.error) ?? 0) && (
        <Button
          type="button"
          variant="outline"
          onClick={() => guard.confirmLeave(() => void refreshReviews(client))}
        >
          Pobierz aktualne dane
        </Button>
      )}
    </div>
  );
}
