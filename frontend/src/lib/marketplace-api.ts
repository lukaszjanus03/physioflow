import { apiClient } from '@/lib/api-client';
import type { TherapistEntry } from '@/lib/portal-api';
import type {
  MarketplaceData,
  Specialization,
} from '@/lib/therapist-profile-api';
import type { PaginatedList } from '@/types/api';

export type PublicTherapist = TherapistEntry & {
  marketplace: MarketplaceData;
  photoUrl: string | null;
};

export type FavoriteTherapist = {
  therapistId: string;
  createdAt: string;
  isAvailable: boolean;
  profile: PublicTherapist | null;
};

export type CatalogFilters = {
  search: string;
  city: string;
  specialization: string;
  pageNumber: number;
};

export const publicQueryOptions = {
  staleTime: 0,
  gcTime: 0,
  retry: false,
  refetchOnMount: 'always',
  refetchOnWindowFocus: 'always',
  refetchOnReconnect: 'always',
} as const;

export class MarketplaceError extends Error {
  constructor(
    public status: number,
    message: string,
    public retryAt = 0,
  ) {
    super(message);
  }
}

const baseUrl = (
  process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000'
).replace(/\/$/, '');

let publicRetryAt = 0;

async function publicRequest(
  path: string,
  signal?: AbortSignal,
) {
  if (Date.now() < publicRetryAt) {
    throw new MarketplaceError(
      429,
      'Przekroczono limit odczytów katalogu. Poczekaj przed kolejną próbą.',
      publicRetryAt,
    );
  }

  const timeout = AbortSignal.timeout(20_000);
  let response: Response;

  try {
    response = await fetch(`${baseUrl}${path}`, {
      signal: signal
        ? AbortSignal.any([signal, timeout])
        : timeout,
      cache: 'no-store',
      credentials: 'omit',
    });
  } catch (error) {
    if (signal?.aborted) throw error;

    throw new MarketplaceError(
      0,
      'Nie udało się połączyć z API. Sprawdź połączenie i spróbuj ponownie.',
    );
  }

  if (!response.ok) {
    let message =
      'Nie udało się pobrać danych. Spróbuj ponownie.';

    try {
      const problem = (await response.json()) as {
        detail?: string;
        errors?: Record<string, string[]>;
      };

      const fields = Object.values(problem.errors ?? {})
        .flat()
        .filter((value) => typeof value === 'string');

      if (fields.length) {
        message = fields.join(' ');
      } else if (typeof problem.detail === 'string') {
        message = problem.detail;
      }
    } catch {
      // Odpowiedz nie musi zawierac JSON.
    }

    if (response.status === 429) {
      const value = response.headers.get('Retry-After');
      const numeric =
        value === null ? Number.NaN : Number(value);
      const parsed = value
        ? Date.parse(value)
        : Number.NaN;

      const seconds = Number.isFinite(numeric)
        ? Math.max(1, numeric)
        : Number.isFinite(parsed)
          ? Math.max(1, (parsed - Date.now()) / 1000)
          : 60;

      publicRetryAt = Date.now() + seconds * 1000;

      throw new MarketplaceError(
        429,
        'Przekroczono limit odczytów katalogu. Poczekaj przed kolejną próbą.',
        publicRetryAt,
      );
    }

    throw new MarketplaceError(
      response.status,
      message,
    );
  }

  return response;
}

export function readCatalogFilters(
  params: Pick<URLSearchParams, 'get'>,
): CatalogFilters {
  const search = params.get('search') ?? '';
  const city = params.get('city') ?? '';
  const specialization =
    params.get('specialization') ?? '';
  const rawPage = params.get('pageNumber') ?? '1';

  if (
    search.length > 100 ||
    city.length > 100 ||
    specialization.length > 50 ||
    !/^[1-9][0-9]*$/.test(rawPage) ||
    Number(rawPage) > Math.floor(2147483647 / 6)
  ) {
    throw new MarketplaceError(
      400,
      'Nieprawidłowe filtry lub numer strony. Wyczyść filtry i spróbuj ponownie.',
    );
  }

  return {
    search,
    city,
    specialization,
    pageNumber: Number(rawPage),
  };
}

export const marketplaceKeys = {
  lists: ['public-therapists'] as const,

  profile: (id: string) =>
    ['public-therapist', id] as const,

  favoriteLists: (accountId: string) =>
    ['favorite-therapists', accountId] as const,

  favoriteIndex: (accountId: string) =>
    ['favorite-therapist-ids', accountId] as const,
};

const favoritesUrl =
  '/api/patient-accounts/me/favorite-therapists';

export const marketplaceApi = {
  catalog: async (
    filters: CatalogFilters,
    signal?: AbortSignal,
  ): Promise<PaginatedList<PublicTherapist>> => {
    const params = new URLSearchParams({
      ...filters,
      pageNumber: String(filters.pageNumber),
      pageSize: '6',
    });

    const response = await publicRequest(
      `/api/therapists?${params}`,
      signal,
    );

    return response.json();
  },

  profile: async (
    id: string,
    signal?: AbortSignal,
  ): Promise<PublicTherapist> => {
    const response = await publicRequest(
      `/api/therapists/${encodeURIComponent(id)}`,
      signal,
    );

    return response.json();
  },

  specializations: async (
    signal?: AbortSignal,
  ): Promise<Specialization[]> => {
    const response = await publicRequest(
      '/api/therapists/specializations',
      signal,
    );

    return response.json();
  },

  photo: async (
    id: string,
    signal?: AbortSignal,
  ): Promise<Blob> => {
    const response = await publicRequest(
      `/api/therapists/${encodeURIComponent(id)}/photo`,
      signal,
    );

    return response.blob();
  },

  favorites: (
    pageNumber: number,
    signal?: AbortSignal,
    pageSize = 6,
  ) =>
    apiClient
      .get<PaginatedList<FavoriteTherapist>>(
        favoritesUrl,
        {
          params: { pageNumber, pageSize },
          signal,
          timeout: 15_000,
        },
      )
      .then((response) => response.data),

  favoriteIds: async (signal?: AbortSignal) => {
    const ids = new Set<string>();

    for (let page = 1; ; page += 1) {
      const result = await marketplaceApi.favorites(
        page,
        signal,
        50,
      );

      for (const item of result.items) {
        ids.add(item.therapistId);
      }

      if (!result.hasNext) {
        return [...ids];
      }
    }
  },

  addFavorite: (id: string) =>
    apiClient.put<void>(
      `${favoritesUrl}/${encodeURIComponent(id)}`,
      undefined,
      { timeout: 15_000 },
    ),

  removeFavorite: (id: string) =>
    apiClient.delete<void>(
      `${favoritesUrl}/${encodeURIComponent(id)}`,
      { timeout: 15_000 },
    ),
};