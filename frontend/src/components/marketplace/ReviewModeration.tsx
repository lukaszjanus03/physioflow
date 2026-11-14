'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useId, useRef, useState } from 'react';
import Link from 'next/link';

import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import {
  ReviewFailure,
  ReviewPages,
} from '@/components/marketplace/TherapistReviews';
import { useMarketplaceSession } from '@/hooks/marketplace-hooks';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { useCurrentTime } from '@/hooks/use-current-time';
import { publicQueryOptions } from '@/lib/marketplace-api';
import {
  refreshReviews,
  reviewDate,
  reviewError,
  reviewLabels,
  reviewRetryAt,
  reviewStatus,
  reviewsApi,
} from '@/lib/reviews-api';
import type {
  Review,
  ReviewStatus,
} from '@/lib/reviews-api';
import { useAuthStore } from '@/stores/auth-store';

export default function ReviewModeration() {
  const session = useMarketplaceSession();
  const accountId = useAuthStore(
    (state) => state.userId,
  );

  const [filter, setFilter] = useState<{
    status: ReviewStatus;
    page: number;
  }>({
    status: 'Pending',
    page: 1,
  });

  const query = useQuery({
    queryKey: [
      'review-moderation',
      accountId,
      filter,
    ],
    queryFn: ({ signal }) =>
      reviewsApi.queue(
        filter.status,
        filter.page,
        signal,
      ),
    enabled: session.loggedIn,
    ...publicQueryOptions,
  });

  return (
    <section className="space-y-6">
      <header className="space-y-3">
        <h1 className="text-4xl">
          Moderacja opinii
        </h1>

        <p className="text-sm text-muted-foreground">
          Uprawnienia są sprawdzane przez serwer.
          Kolejka nie zawiera własnych opinii ani
          opinii o Twoim profilu.
        </p>
      </header>

      {!session.ready ? (
        <LoadingState title="Sprawdzanie sesji…" />
      ) : !session.loggedIn ? (
        <p>
          Zaloguj się, a następnie wróć pod adres{' '}
          <strong>/moderacja/opinie</strong>.{' '}
          <Link
            href="/login"
            className="text-primary underline"
          >
            Przejdź do logowania
          </Link>
        </p>
      ) : (
        <>
          <nav
            aria-label="Status opinii"
            className="flex flex-wrap gap-3"
          >
            {(
              [
                'Pending',
                'Approved',
                'Rejected',
              ] as const
            ).map((status) => (
              <Button
                key={status}
                variant={
                  filter.status === status
                    ? 'default'
                    : 'outline'
                }
                type="button"
                aria-pressed={
                  filter.status === status
                }
                onClick={() =>
                  setFilter({
                    status,
                    page: 1,
                  })
                }
              >
                {reviewLabels[status]}
              </Button>
            ))}

            <Button
              variant="outline"
              type="button"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              Odśwież
            </Button>
          </nav>

          {query.isError ? (
            <ReviewFailure
              error={query.error}
              retry={() => void query.refetch()}
            />
          ) : query.isPending ? (
            <LoadingState title="Pobieranie kolejki moderacji…" />
          ) : (
            <>
              {!query.data.items.length && (
                <p className="rounded-xl border p-6">
                  Brak opinii na tej stronie.
                </p>
              )}

              <div className="space-y-5">
                {query.data.items.map((review) => (
                  <Decision
                    key={`${accountId}:${review.id}:${review.version}`}
                    review={review}
                  />
                ))}
              </div>

              {(query.data.totalPages > 1 ||
                filter.page > 1) && (
                <ReviewPages
                  page={filter.page}
                  pages={query.data.totalPages}
                  next={query.data.hasNext}
                  busy={query.isFetching}
                  onChange={(page) =>
                    setFilter((old) => ({
                      ...old,
                      page,
                    }))
                  }
                />
              )}
            </>
          )}
        </>
      )}
    </section>
  );
}

function Decision({
  review,
}: {
  review: Review;
}) {
  const client = useQueryClient();
  const id = useId();

  const [reason, setReason] = useState('');
  const [checked, setChecked] = useState(false);
  const [message, setMessage] = useState('');
  const [conflict, setConflict] = useState(false);
  const [retryAt, setRetryAt] = useState(0);

  const now = useCurrentTime();

  const wait = Math.max(
    0,
    Math.ceil((retryAt - now) / 1000),
  );

  const lock = useRef(false);

  const mutation = useMutation({
    mutationFn: (
      action: 'approve' | 'reject' | 'withdraw',
    ) =>
      reviewsApi.decide(
        review,
        action,
        reason,
      ),
    retry: false,
    onSuccess: async () => {
      setReason('');
      await refreshReviews(client);
    },
    onError: (error) => {
      setMessage(reviewError(error));
      setConflict(
        [404, 409].includes(
          reviewStatus(error) ?? 0,
        ),
      );
      setRetryAt(reviewRetryAt(error));
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  const guard = useUnsavedChanges(
    reason.length > 0,
    mutation.isPending,
  );

  function decide(
    action: 'approve' | 'reject' | 'withdraw',
  ) {
    if (
      lock.current ||
      mutation.isPending ||
      conflict ||
      wait > 0
    ) {
      return;
    }

    if (!checked) {
      setMessage(
        'Najpierw przeczytaj opinię i potwierdź weryfikację.',
      );
      return;
    }

    if (
      action !== 'approve' &&
      (!reason.trim() || reason.length > 1000)
    ) {
      setMessage(
        'Wpisz uzasadnienie decyzji: od 1 do 1000 znaków.',
      );
      return;
    }

    lock.current = true;
    setMessage('');
    mutation.mutate(action);
  }

  return (
    <article className="space-y-4 rounded-2xl border bg-card p-5 sm:p-6">
      {guard.discardDialog}

      <div className="flex flex-wrap justify-between gap-3">
        <p className="font-semibold">
          Ocena: {review.rating}/5 ·{' '}
          {reviewLabels[review.status]}
        </p>

        <span className="text-sm text-muted-foreground">
          {reviewDate(
            review.updatedAt ?? review.createdAt,
          )}
        </span>
      </div>

      <Link
        href={`/terapeuci/${review.therapistId}`}
        prefetch={false}
        className="break-all text-sm text-primary underline"
      >
        Profil terapeuty: {review.therapistId}
      </Link>

      <p className="whitespace-pre-wrap break-words rounded-xl bg-muted/40 p-4 text-sm leading-relaxed">
        {review.comment ||
          'Opinia bez komentarza.'}
      </p>

      {review.rejectionReason && (
        <p className="whitespace-pre-wrap break-words text-sm">
          Uzasadnienie wcześniejszej decyzji:{' '}
          {review.rejectionReason}
        </p>
      )}

      {review.status !== 'Rejected' && (
        <fieldset
          className="space-y-4"
          disabled={
            mutation.isPending ||
            conflict ||
            wait > 0
          }
        >
          <legend className="sr-only">
            Decyzja moderacyjna
          </legend>

          <label className="flex items-start gap-3 text-sm">
            <input
              type="checkbox"
              checked={checked}
              onChange={(event) =>
                setChecked(event.target.checked)
              }
              className="mt-1"
            />
            Przeczytano opinię i sprawdzono treść pod
            kątem danych kontaktowych oraz
            szczegółowej dokumentacji medycznej.
          </label>

          <div className="space-y-2">
            <label
              htmlFor={id}
              className="text-sm font-medium"
            >
              Uzasadnienie (wymagane przy odrzuceniu
              lub wycofaniu)
            </label>

            <Textarea
              id={id}
              value={reason}
              maxLength={1000}
              onChange={(event) =>
                setReason(event.target.value)
              }
            />

            <p className="text-xs text-muted-foreground">
              {reason.length}/1000. Nie kopiuj tutaj
              danych wrażliwych z opinii.
            </p>
          </div>

          <div className="flex flex-wrap gap-3">
            {review.status === 'Pending' ? (
              <>
                <Button
                  type="button"
                  onClick={() => decide('approve')}
                >
                  Zatwierdź
                </Button>

                <Button
                  type="button"
                  variant="destructive"
                  onClick={() => decide('reject')}
                >
                  Odrzuć
                </Button>
              </>
            ) : (
              <Button
                type="button"
                variant="destructive"
                onClick={() => decide('withdraw')}
              >
                Wycofaj z publikacji
              </Button>
            )}
          </div>
        </fieldset>
      )}

      {mutation.isPending && (
        <p role="status" className="text-sm">
          Zapisywanie decyzji…
        </p>
      )}

      {message && (
        <p
          role="alert"
          className="text-sm text-destructive"
        >
          {message}
        </p>
      )}

      {wait > 0 && (
        <p role="status" className="text-sm">
          Ponowna próba za {wait} s.
        </p>
      )}

      {conflict && (
        <Button
          type="button"
          variant="outline"
          onClick={() =>
            guard.confirmLeave(() =>
              void refreshReviews(client),
            )
          }
        >
          Pobierz aktualną treść przed kolejną decyzją
        </Button>
      )}
    </article>
  );
}