import { isAxiosError } from 'axios';
import type { QueryClient } from '@tanstack/react-query';

import { apiClient } from '@/lib/api-client';
import { publicRequest } from '@/lib/marketplace-api';
import type { PaginatedList } from '@/types/api';

export type ReviewStatus = 'Pending' | 'Approved' | 'Rejected';

export const reviewLabels: Record<ReviewStatus, string> = {
  Pending: 'Oczekuje na moderację',
  Approved: 'Opublikowana',
  Rejected: 'Odrzucona',
};

export type Review = {
  id: string;
  therapistId: string;
  rating: number;
  comment: string | null;
  status: ReviewStatus;
  createdAt: string;
  updatedAt: string | null;
  decidedAt: string | null;
  rejectionReason: string | null;
  version: string;
};

export type Eligibility = {
  canCreate: boolean;
  reasonCode: string | null;
  review: Review | null;
};

export type PublicReviews = {
  averageRating: number | null;
  reviewCount: number;
  reviews: PaginatedList<{
    rating: number;
    comment: string | null;
    date: string;
    firstName: string;
    lastNameInitial: string | null;
  }>;
};

const own = (id: string) =>
  `/api/patient-accounts/me/therapist-reviews/${encodeURIComponent(id)}`;

const moderation = '/api/moderation/therapist-reviews';

export const reviewsApi = {
  public: async (
    id: string,
    page: number,
    signal: AbortSignal,
  ): Promise<PublicReviews> => {
    const response = await publicRequest(
      `/api/therapists/${encodeURIComponent(id)}/reviews?pageNumber=${page}&pageSize=6`,
      signal,
    );

    return response.json();
  },

  own: (id: string, signal: AbortSignal) =>
    apiClient
      .get<Eligibility>(own(id), {
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  save: (
    id: string,
    edit: boolean,
    body: {
      rating: number;
      comment: string | null;
    },
  ) =>
    edit
      ? apiClient.put<Review>(own(id), body, {
          timeout: 15_000,
        })
      : apiClient.post<Review>(own(id), body, {
          timeout: 15_000,
        }),

  remove: (id: string) =>
    apiClient.delete(own(id), {
      timeout: 15_000,
    }),

  queue: (
    status: ReviewStatus,
    page: number,
    signal: AbortSignal,
  ) =>
    apiClient
      .get<PaginatedList<Review>>(moderation, {
        params: {
          status,
          pageNumber: page,
          pageSize: 10,
        },
        signal,
        timeout: 15_000,
      })
      .then((response) => response.data),

  decide: (
    review: Review,
    action: 'approve' | 'reject' | 'withdraw',
    reason: string,
  ) =>
    apiClient.post<Review>(
      `${moderation}/${review.id}/${action}`,
      {
        version: review.version,
        ...(action === 'approve'
          ? {}
          : { reason: reason.trim() }),
      },
      { timeout: 15_000 },
    ),
};

export function reviewStatus(error: unknown) {
  return isAxiosError(error)
    ? error.response?.status
    : undefined;
}

export function reviewError(error: unknown) {
  if (!isAxiosError(error)) {
    return 'Nie udało się połączyć z serwerem. Spróbuj ponownie.';
  }

  const body = error.response?.data as
    | {
        code?: string;
        errorCodes?: Record<string, string[]>;
      }
    | undefined;

  const messages: Record<string, string> = {
    review_rating_invalid: 'Wybierz ocenę od 1 do 5.',
    review_comment_too_long:
      'Komentarz może mieć maksymalnie 1000 znaków.',
    review_contact_details:
      'Usuń z komentarza dane kontaktowe, linki i długie ciągi cyfr.',
    review_completed_visit_required:
      'Do wystawienia lub edycji opinii potrzebna jest odbyta wizyta.',
    review_already_exists:
      'Masz już opinię o tym terapeucie. Odśwież dane.',
    review_status_conflict:
      'Status opinii się zmienił. Pobierz aktualną treść.',
    concurrent_modification:
      'Opinia została zmieniona. Pobierz aktualną treść.',
    review_reason_required:
      'Wpisz uzasadnienie decyzji.',
    review_reason_too_long:
      'Uzasadnienie może mieć maksymalnie 1000 znaków.',
    review_version_required:
      'Odśwież opinię przed podjęciem decyzji.',
    email_confirmation_required:
      'Potwierdź adres e-mail.',
    review_self_moderation_forbidden:
      'Nie możesz moderować własnych opinii ani opinii o swoim profilu.',
  };

  const codes = Object.values(body?.errorCodes ?? {}).flat();

  if (body?.code) {
    codes.push(body.code);
  }

  const matched = [
    ...new Set(
      codes
        .map((code) => messages[code])
        .filter(Boolean),
    ),
  ];

  if (matched.length) {
    return matched.join(' ');
  }

  switch (error.response?.status) {
    case 400:
      return 'Sprawdź poprawność pól formularza.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Nie masz uprawnień do tej operacji.';
    case 404:
      return 'Profil lub opinia nie są dostępne. Odśwież dane.';
    case 409:
      return 'Dane zostały zmienione. Odśwież je przed ponowną próbą.';
    case 429:
      return 'Przekroczono limit prób. Poczekaj przed kolejną próbą.';
    default:
      return 'Nie udało się wykonać operacji. Spróbuj ponownie.';
  }
}

export function reviewRetryAt(
  error: unknown,
  fallbackSeconds = 60,
) {
  if (reviewStatus(error) !== 429) {
    return 0;
  }

  const value = isAxiosError(error)
    ? error.response?.headers['retry-after']
    : undefined;

  const raw = typeof value === 'string' ? value : '';
  const seconds = raw ? Number(raw) : Number.NaN;
  const date = Date.parse(raw);

  return (
    Date.now() +
    (Number.isFinite(seconds)
      ? Math.max(1, seconds) * 1000
      : Number.isFinite(date)
        ? Math.max(1000, date - Date.now())
        : fallbackSeconds * 1000)
  );
}

export async function refreshReviews(client: QueryClient) {
  await client.invalidateQueries({
    predicate: (query) =>
      [
        'therapist-reviews',
        'own-therapist-review',
        'review-moderation',
        'public-therapists',
        'public-therapist',
        'favorite-therapists',
      ].includes(String(query.queryKey[0])),
  });
}

export function reviewDate(value: string) {
  return new Intl.DateTimeFormat('pl-PL', {
    dateStyle: 'medium',
    timeZone: 'Europe/Warsaw',
  }).format(new Date(value));
}