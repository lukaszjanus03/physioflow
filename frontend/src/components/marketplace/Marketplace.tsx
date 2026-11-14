'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Heart, HeartPulse, Loader2, MapPin, Search, UserRound } from 'lucide-react';
import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { toast } from 'sonner';

import PatientBookingDialog from '@/components/patient-portal/PatientBookingDialog';
import { SendCooperationRequest } from '@/components/patient-portal/PatientCooperation';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { Input, Label } from '@/components/ui/primitives';
import { useCurrentTime } from '@/hooks/use-current-time';
import { RatingSummary } from '@/components/marketplace/TherapistReviews';
import { useFavoriteIndex, useMarketplaceSession } from '@/hooks/marketplace-hooks';
import { cooperationApi, cooperationKeys } from '@/lib/cooperation-api';
import {
  marketplaceApi,
  marketplaceKeys,
  MarketplaceError,
  publicQueryOptions,
  readCatalogFilters,
} from '@/lib/marketplace-api';
import type { CatalogFilters, PublicTherapist } from '@/lib/marketplace-api';
import { rememberMarketplaceReturn, safeCatalogBack } from '@/lib/marketplace-navigation';
import { getApiMessage, getApiStatus } from '@/lib/portal-api';

export function MarketplaceFrame({ children }: { children: ReactNode }) {
  const session = useMarketplaceSession();

  return (
    <div className="min-h-screen bg-muted/20">
      <header className="border-b bg-card/95">
        <nav
          aria-label="Marketplace"
          className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-6"
        >
          <Link href="/" className="flex items-center gap-3">
            <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary text-primary-foreground">
              <HeartPulse aria-hidden="true" className="h-5 w-5" />
            </span>
            <span className="font-semibold">PhysioFlow</span>
          </Link>

          <div className="flex flex-wrap items-center gap-3 text-sm">
            <Link href="/terapeuci" prefetch={false} className="hover:text-primary">
              Znajdź terapeutę
            </Link>

            {session.patientId && (
              <Link href="/portal?view=favorites" prefetch={false} className="hover:text-primary">
                Ulubieni
              </Link>
            )}

            <Button variant="outline" size="sm" asChild>
              <Link href={session.loggedIn ? (session.therapist ? '/dashboard' : '/portal') : '/login'}>
                {session.loggedIn ? 'Mój panel' : 'Zaloguj się'}
              </Link>
            </Button>
          </div>
        </nav>
      </header>

      <main className="mx-auto max-w-6xl space-y-6 px-4 py-8 sm:px-6">{children}</main>
    </div>
  );
}

function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const now = useCurrentTime();

  const status = error instanceof MarketplaceError ? error.status : getApiStatus(error);

  const retryAt = error instanceof MarketplaceError ? error.retryAt : 0;

  const wait = Math.max(0, Math.ceil((retryAt - now) / 1000));

  const message =
    status === 404
      ? 'Profil nie istnieje lub nie jest już dostępny.'
      : status === 429
        ? 'Przekroczono limit żądań. Odczekaj przed kolejną próbą.'
        : error instanceof MarketplaceError
          ? error.message
          : getApiMessage(error);

  return (
    <section className="space-y-4 rounded-2xl border bg-card p-6">
      <h2 className="text-3xl">{status === 404 ? 'Profil niedostępny' : 'Nie udało się pobrać danych'}</h2>

      <p role="alert" className="text-sm text-destructive">
        {message}
      </p>

      {wait > 0 && (
        <p role="status" className="text-sm text-muted-foreground">
          Ponowna próba za {wait} s.
        </p>
      )}

      {onRetry && status !== 404 && (
        <Button type="button" variant="outline" disabled={wait > 0} onClick={onRetry}>
          Spróbuj ponownie
        </Button>
      )}
    </section>
  );
}

function PhotoBlob({ blob }: { blob: Blob }) {
  const image = useRef<HTMLImageElement>(null);

  useEffect(() => {
    const element = image.current;
    if (!element) return;

    const url = URL.createObjectURL(blob);
    element.src = url;

    return () => {
      element.removeAttribute('src');
      URL.revokeObjectURL(url);
    };
  }, [blob]);

  // eslint-disable-next-line @next/next/no-img-element -- Public no-store photos are displayed from short-lived blob URLs.
  return <img ref={image} alt="Zdjęcie profilowe terapeuty" className="h-full w-full object-cover" />;
}

function ProfilePhoto({
  profile,
  version,
  large = false,
}: {
  profile: PublicTherapist;
  version: number;
  large?: boolean;
}) {
  const query = useQuery({
    queryKey: ['public-therapist-photo', profile.id, version],
    queryFn: ({ signal }) => marketplaceApi.photo(profile.id, signal),
    enabled: Boolean(profile.photoUrl),
    ...publicQueryOptions,
  });

  return (
    <div
      className={`flex shrink-0 items-center justify-center overflow-hidden rounded-2xl border bg-primary/5 text-primary ${
        large ? 'h-36 w-36' : 'h-20 w-20'
      }`}
    >
      {profile.photoUrl && query.isSuccess ? (
        <PhotoBlob blob={query.data} />
      ) : (
        <UserRound aria-hidden="true" className={large ? 'h-14 w-14' : 'h-9 w-9'} />
      )}

      <span className="sr-only">
        {query.isError ? 'Podgląd zdjęcia niedostępny' : !profile.photoUrl ? 'Brak zdjęcia' : ''}
      </span>
    </div>
  );
}

function FavoriteButton({ id, removeOnly = false }: { id: string; removeOnly?: boolean }) {
  const session = useMarketplaceSession();
  const client = useQueryClient();

  const index = useFavoriteIndex(session.patientId, !removeOnly);

  const mutation = useMutation({
    mutationFn: (remove: boolean) =>
      remove ? marketplaceApi.removeFavorite(id) : marketplaceApi.addFavorite(id),
    retry: false,
    onMutate: async () => {
      if (session.patientId) {
        await client.cancelQueries({
          queryKey: marketplaceKeys.favoriteIndex(session.patientId),
        });
      }
    },
    onSuccess: async (_data, remove) => {
      if (!session.patientId) return;

      client.setQueryData<string[]>(marketplaceKeys.favoriteIndex(session.patientId), (old) =>
        remove ? old?.filter((value) => value !== id) : [...new Set([...(old ?? []), id])],
      );

      await Promise.all([
        client.invalidateQueries({
          queryKey: marketplaceKeys.favoriteIndex(session.patientId),
        }),
        client.invalidateQueries({
          queryKey: marketplaceKeys.favoriteLists(session.patientId),
        }),
      ]);
    },
    onError: async (error) => {
      if (getApiStatus(error) === 404) {
        await Promise.all([
          client.invalidateQueries({
            queryKey: marketplaceKeys.lists,
          }),
          client.invalidateQueries({
            queryKey: marketplaceKeys.profile(id),
          }),
        ]);
      }
    },
  });

  if (!session.patientId) return null;

  const saved = removeOnly || Boolean(index.data?.includes(id));

  return (
    <div className="space-y-2">
      <Button
        type="button"
        variant="outline"
        aria-pressed={saved}
        disabled={mutation.isPending || (!removeOnly && (index.isPending || index.isError))}
        onClick={() => mutation.mutate(saved)}
      >
        {mutation.isPending || (!removeOnly && index.isPending) ? (
          <Loader2 aria-hidden="true" className="mr-2 h-4 w-4 animate-spin" />
        ) : (
          <Heart aria-hidden="true" className={`mr-2 h-4 w-4 ${saved ? 'fill-primary text-primary' : ''}`} />
        )}

        {mutation.isPending
          ? 'Zapisywanie…'
          : !removeOnly && index.isPending
            ? 'Sprawdzanie ulubionych…'
            : saved
              ? 'Usuń z ulubionych'
              : 'Dodaj do ulubionych'}
      </Button>

      {!removeOnly && index.isError && (
        <div className="space-y-2">
          <p role="alert" className="text-xs text-destructive">
            Nie udało się sprawdzić ulubionych.
          </p>

          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={index.isFetching}
            onClick={() => void index.refetch()}
          >
            Sprawdź ponownie
          </Button>
        </div>
      )}

      {mutation.isError && (
        <p role="alert" className="text-xs text-destructive">
          {getApiMessage(mutation.error)}
        </p>
      )}
    </div>
  );
}

function ProfileCard({
  profile,
  back,
  version,
  labels,
  favoriteList = false,
}: {
  profile: PublicTherapist;
  back: string;
  version: number;
  labels: Map<string, string>;
  favoriteList?: boolean;
}) {
  return (
    <article className="flex min-w-0 flex-col gap-4 rounded-2xl border bg-card p-5 shadow-sm">
      <div className="flex items-start gap-4">
        <ProfilePhoto profile={profile} version={version} />

        <div className="min-w-0">
          <h2 className="break-words text-2xl">
            {profile.firstName} {profile.lastName}
          </h2>

          <div className="mt-2">
            <RatingSummary averageRating={profile.averageRating} reviewCount={profile.reviewCount} />
          </div>

          <p className="mt-1 break-words text-sm font-medium">{profile.practiceName}</p>

          <p className="mt-2 flex items-center gap-1 text-sm text-muted-foreground">
            <MapPin aria-hidden="true" className="h-4 w-4 shrink-0" />
            {profile.city}
          </p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        {profile.marketplace.specializations.map((code) => (
          <span key={code} className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
            {labels.get(code) ?? code}
          </span>
        ))}
      </div>

      <p className="line-clamp-3 flex-1 break-words text-sm leading-relaxed text-muted-foreground">
        {profile.bio || 'Terapeuta nie dodał jeszcze opisu.'}
      </p>

      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link
            prefetch={false}
            href={`/terapeuci/${profile.id}?${new URLSearchParams({
              back,
            })}`}
          >
            Zobacz profil
          </Link>
        </Button>

        <FavoriteButton id={profile.id} removeOnly={favoriteList} />
      </div>
    </article>
  );
}

function Pagination({
  page,
  pages,
  hasNext,
  fetching,
  onPage,
}: {
  page: number;
  pages: number;
  hasNext: boolean;
  fetching: boolean;
  onPage: (page: number) => void;
}) {
  return (
    <nav aria-label="Strony wyników" className="flex flex-wrap items-center justify-between gap-3">
      <Button
        type="button"
        variant="outline"
        disabled={page <= 1 || fetching}
        onClick={() => onPage(page - 1)}
      >
        Poprzednia
      </Button>

      <span className="text-sm text-muted-foreground">
        Strona {page} z {Math.max(1, pages)}
      </span>

      <Button
        type="button"
        variant="outline"
        disabled={!hasNext || fetching}
        onClick={() => onPage(page + 1)}
      >
        Następna
      </Button>
    </nav>
  );
}

export function MarketplaceCatalog({ embedded = false }: { embedded?: boolean }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const formId = useId();

  let filters: CatalogFilters = {
    search: '',
    city: '',
    specialization: '',
    pageNumber: 1,
  };

  let validation: unknown;

  try {
    filters = readCatalogFilters(params);
  } catch (error) {
    validation = error;
  }

  const dictionary = useQuery({
    queryKey: ['public-specializations'],
    queryFn: ({ signal }) => marketplaceApi.specializations(signal),
    ...publicQueryOptions,
  });

  const query = useQuery({
    queryKey: [...marketplaceKeys.lists, filters],
    queryFn: ({ signal }) => marketplaceApi.catalog(filters, signal),
    enabled: !validation,
    ...publicQueryOptions,
    refetchInterval: 60_000,
  });

  const labels = new Map(dictionary.data?.map((item) => [item.code, item.label]));

  const back = `${pathname}${params.size ? `?${params}` : ''}`;

  function navigate(next: CatalogFilters) {
    const search = new URLSearchParams();

    if (embedded) search.set('view', 'catalog');
    if (next.search) search.set('search', next.search);
    if (next.city) search.set('city', next.city);

    if (next.specialization) {
      search.set('specialization', next.specialization);
    }

    if (next.pageNumber > 1) {
      search.set('pageNumber', String(next.pageNumber));
    }

    router.push(`${pathname}${search.size ? `?${search}` : ''}`, { scroll: false });
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const form = new FormData(event.currentTarget);

    navigate({
      search: String(form.get('search') ?? '').trim(),
      city: String(form.get('city') ?? '').trim(),
      specialization: String(form.get('specialization') ?? ''),
      pageNumber: 1,
    });
  }

  return (
    <section className="space-y-6">
      <header className="rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-6 sm:p-8">
        <p className="text-xs font-semibold uppercase tracking-widest text-primary">Terapeuci w PhysioFlow</p>

        <h1 className="mt-3 text-4xl sm:text-5xl">Znajdź swojego terapeutę</h1>

        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-muted-foreground">
          Poznaj specjalizacje, doświadczenie i gabinety. Katalog możesz przeglądać bez konta. Współpraca i
          rezerwacje wymagają konta pacjenta.
        </p>
      </header>

      <form
        key={`${filters.search}|${filters.city}|${filters.specialization}`}
        onSubmit={submit}
        className="grid gap-4 rounded-2xl border bg-card p-5 sm:grid-cols-2 lg:grid-cols-3"
      >
        <div className="space-y-2">
          <Label htmlFor={`${formId}-search`}>Imię, nazwisko lub gabinet</Label>

          <Input
            id={`${formId}-search`}
            name="search"
            type="search"
            defaultValue={filters.search}
            maxLength={100}
            placeholder="Np. Anna Nowak"
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor={`${formId}-city`}>Miejscowość — pełna nazwa</Label>

          <Input
            id={`${formId}-city`}
            name="city"
            defaultValue={filters.city}
            maxLength={100}
            placeholder="Np. Kraków"
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor={`${formId}-specialization`}>Specjalizacja</Label>

          <select
            id={`${formId}-specialization`}
            name="specialization"
            defaultValue={filters.specialization}
            className="h-10 w-full rounded-md border bg-background px-3 text-sm"
          >
            <option value="">Wszystkie specjalizacje</option>

            {filters.specialization &&
              !dictionary.data?.some((item) => item.code === filters.specialization) && (
                <option value={filters.specialization}>{filters.specialization}</option>
              )}

            {dictionary.data?.map((item) => (
              <option key={item.code} value={item.code}>
                {item.label}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-wrap gap-3 sm:col-span-2 lg:col-span-3">
          <Button type="submit" disabled={query.isFetching}>
            <Search aria-hidden="true" className="mr-2 h-4 w-4" />
            Szukaj
          </Button>

          <Button
            type="button"
            variant="outline"
            onClick={() =>
              navigate({
                search: '',
                city: '',
                specialization: '',
                pageNumber: 1,
              })
            }
          >
            Wyczyść filtry
          </Button>
        </div>
      </form>

      {dictionary.isError && (
        <ErrorState error={dictionary.error} onRetry={() => void dictionary.refetch()} />
      )}

      {(filters.search || filters.city || filters.specialization) && (
        <p className="rounded-xl bg-primary/5 p-3 text-sm">
          Aktywne filtry:{' '}
          {[
            filters.search && `fraza: ${filters.search}`,
            filters.city && `miejscowość: ${filters.city}`,
            filters.specialization &&
              `specjalizacja: ${labels.get(filters.specialization) ?? filters.specialization}`,
          ]
            .filter(Boolean)
            .join(' · ')}
        </p>
      )}

      {validation ? (
        <ErrorState error={validation} />
      ) : query.isPending ? (
        <LoadingState title="Wczytywanie terapeutów…" />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p role="status" className="text-sm text-muted-foreground">
              {query.isFetching ? 'Odświeżanie wyników…' : `Znaleziono profili: ${query.data.totalCount}`}
            </p>

            <Button
              type="button"
              variant="outline"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              Odśwież wyniki
            </Button>
          </div>

          {query.data.items.length ? (
            <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
              {query.data.items.map((profile) => (
                <ProfileCard
                  key={profile.id}
                  profile={profile}
                  back={back}
                  labels={labels}
                  version={query.dataUpdatedAt}
                />
              ))}
            </div>
          ) : (
            <div className="space-y-3 rounded-2xl border border-dashed bg-card p-8 text-center">
              <h2 className="text-3xl">Brak profili do wyświetlenia</h2>

              <p className="text-sm text-muted-foreground">
                {query.data.totalCount
                  ? 'Na tej stronie nie ma wyników. Wróć do pierwszej strony.'
                  : 'Zmień filtry lub wróć później. Katalog pokazuje wyłącznie opublikowane profile.'}
              </p>

              {filters.pageNumber > 1 && (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() =>
                    navigate({
                      ...filters,
                      pageNumber: 1,
                    })
                  }
                >
                  Pierwsza strona
                </Button>
              )}
            </div>
          )}

          {(query.data.totalPages > 1 || filters.pageNumber > 1) && (
            <Pagination
              page={filters.pageNumber}
              pages={query.data.totalPages}
              hasNext={query.data.hasNext}
              fetching={query.isFetching}
              onPage={(pageNumber) => navigate({ ...filters, pageNumber })}
            />
          )}
        </>
      )}
    </section>
  );
}

export function MarketplaceFavorites() {
  const session = useMarketplaceSession();
  const router = useRouter();
  const params = useSearchParams();

  const raw = params.get('pageNumber') ?? '1';

  const valid = /^[1-9][0-9]*$/.test(raw) && Number(raw) <= Math.floor(2147483647 / 6);

  const page = valid ? Number(raw) : 1;

  const query = useQuery({
    queryKey: [...marketplaceKeys.favoriteLists(session.patientId ?? ''), page],
    queryFn: ({ signal }) => marketplaceApi.favorites(page, signal),
    enabled: Boolean(session.patientId) && valid,
    ...publicQueryOptions,
  });

  const dictionary = useQuery({
    queryKey: ['public-specializations'],
    queryFn: ({ signal }) => marketplaceApi.specializations(signal),
    enabled: Boolean(session.patientId),
    ...publicQueryOptions,
  });

  const labels = new Map(dictionary.data?.map((item) => [item.code, item.label]));

  const back = `/portal?view=favorites${page > 1 ? `&pageNumber=${page}` : ''}`;

  return (
    <section className="space-y-6">
      <header>
        <h1 className="text-4xl">Ulubieni terapeuci</h1>

        <p className="mt-3 text-sm text-muted-foreground">
          Twoja lista zapisanych profili. Dodanie do ulubionych nie rozpoczyna współpracy ani nie rezerwuje
          wizyty.
        </p>
      </header>

      {!session.ready ? (
        <LoadingState title="Sprawdzanie sesji…" />
      ) : !session.patientId ? (
        <p role="alert">Lista jest dostępna wyłącznie dla zalogowanego pacjenta.</p>
      ) : !valid ? (
        <ErrorState error={new MarketplaceError(400, 'Nieprawidłowy numer strony.')} />
      ) : query.isPending ? (
        <LoadingState title="Wczytywanie ulubionych…" />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-muted-foreground">Zapisane profile: {query.data.totalCount}</p>

            <Button
              type="button"
              variant="outline"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              Odśwież listę
            </Button>
          </div>

          {query.data.items.length ? (
            <div className="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
              {query.data.items.map((item) =>
                item.isAvailable && item.profile ? (
                  <ProfileCard
                    key={item.therapistId}
                    profile={item.profile}
                    favoriteList
                    labels={labels}
                    version={query.dataUpdatedAt}
                    back={back}
                  />
                ) : (
                  <article
                    key={item.therapistId}
                    className="space-y-4 rounded-2xl border border-dashed bg-card p-5"
                  >
                    <h2 className="text-2xl">Terapeuta niedostępny</h2>

                    <p className="text-sm text-muted-foreground">
                      Profil nie jest obecnie dostępny. Możesz usunąć go z ulubionych.
                    </p>

                    <FavoriteButton id={item.therapistId} removeOnly />
                  </article>
                ),
              )}
            </div>
          ) : (
            <div className="space-y-4 rounded-2xl border border-dashed bg-card p-8 text-center">
              <h2 className="text-3xl">
                {page > 1 ? 'Ta strona jest pusta' : 'Nie masz jeszcze ulubionych'}
              </h2>

              <Button asChild variant="outline">
                <Link href={page > 1 ? '/portal?view=favorites' : '/portal?view=catalog'}>
                  {page > 1 ? 'Pierwsza strona' : 'Znajdź terapeutę'}
                </Link>
              </Button>
            </div>
          )}

          {(query.data.totalPages > 1 || page > 1) && (
            <Pagination
              page={page}
              pages={query.data.totalPages}
              hasNext={query.data.hasNext}
              fetching={query.isFetching}
              onPage={(next) =>
                router.push(`/portal?view=favorites${next > 1 ? `&pageNumber=${next}` : ''}`, {
                  scroll: false,
                })
              }
            />
          )}
        </>
      )}
    </section>
  );
}

function PatientActions({ accountId, profile }: { accountId: string; profile: PublicTherapist }) {
  const [booking, setBooking] = useState(false);

  const query = useQuery({
    queryKey: cooperationKeys.therapists(accountId),
    queryFn: ({ signal }) => cooperationApi.therapists(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  if (query.isPending) {
    return <LoadingState title="Sprawdzanie współpracy…" className="min-h-24" />;
  }

  if (query.isError) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  }

  const active = query.data.some((item) => item.therapist.id === profile.id);

  return (
    <>
      {active ? (
        <div className="space-y-4 rounded-xl border bg-primary/5 p-5">
          <p className="text-sm">Współpracujesz już z tym terapeutą.</p>

          <Button type="button" onClick={() => setBooking(true)}>
            Zarezerwuj wizytę
          </Button>
        </div>
      ) : (
        <SendCooperationRequest
          accountId={accountId}
          therapistId={profile.id}
          therapistName={`${profile.firstName} ${profile.lastName}`}
        />
      )}

      {booking && (
        <PatientBookingDialog
          accountId={accountId}
          initialTherapistId={profile.id}
          onClose={() => setBooking(false)}
          onFindTherapist={() => setBooking(false)}
          onSaved={() => {
            setBooking(false);
            toast.success('Wizyta została zarezerwowana.');
          }}
        />
      )}
    </>
  );
}

export function MarketplaceProfile({ therapistId }: { therapistId: string }) {
  const params = useSearchParams();
  const session = useMarketplaceSession();

  const back = safeCatalogBack(params.get('back'));

  const path = `/terapeuci/${therapistId}?${new URLSearchParams({ back })}`;

  const valid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(therapistId);

  const query = useQuery({
    queryKey: marketplaceKeys.profile(therapistId),
    queryFn: ({ signal }) => marketplaceApi.profile(therapistId, signal),
    enabled: valid,
    ...publicQueryOptions,
    refetchInterval: 60_000,
  });

  const dictionary = useQuery({
    queryKey: ['public-specializations'],
    queryFn: ({ signal }) => marketplaceApi.specializations(signal),
    enabled: valid,
    ...publicQueryOptions,
  });

  const labels = new Map(dictionary.data?.map((item) => [item.code, item.label]));

  const profile = query.data;

  return (
    <section className="space-y-6">
      <Button asChild variant="outline">
        <Link prefetch={false} href={back}>
          <ArrowLeft aria-hidden="true" className="mr-2 h-4 w-4" />
          Wróć do wyników
        </Link>
      </Button>

      {!valid ? (
        <ErrorState error={new MarketplaceError(404, 'Profil niedostępny.')} />
      ) : query.isPending ? (
        <LoadingState title="Wczytywanie profilu terapeuty…" />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        profile && (
          <>
            <article className="space-y-6 rounded-2xl border bg-card p-6 sm:p-8">
              <header className="flex flex-col gap-5 sm:flex-row sm:items-center">
                <ProfilePhoto profile={profile} version={query.dataUpdatedAt} large />

                <div>
                  <p className="text-xs uppercase tracking-widest text-primary">Profil zawodowy</p>

                  <h1 className="mt-2 break-words text-4xl sm:text-5xl">
                    {profile.firstName} {profile.lastName}
                  </h1>

                  <p className="mt-3 break-words font-medium">{profile.practiceName}</p>

                  <p className="mt-2 text-sm text-muted-foreground">{profile.city}</p>
                </div>
              </header>

              <div className="flex flex-wrap gap-2">
                {profile.marketplace.specializations.map((code) => (
                  <span key={code} className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
                    {labels.get(code) ?? code}
                  </span>
                ))}
              </div>

              <FavoriteButton id={profile.id} />

              <div className="grid gap-6 lg:grid-cols-2">
                {[
                  ['O terapeucie', profile.bio],
                  ['Doświadczenie', profile.marketplace.experience],
                  ['Wykształcenie', profile.marketplace.education],
                  [
                    'Adres gabinetu',
                    [profile.marketplace.street, profile.marketplace.buildingNumber]
                      .filter(Boolean)
                      .join(' ') +
                      '\n' +
                      [profile.marketplace.postalCode, profile.city].filter(Boolean).join(' '),
                  ],
                  ['Informacje organizacyjne', profile.marketplace.organizationalInfo],
                  ['Przygotowanie do pierwszej wizyty', profile.marketplace.firstVisitPreparation],
                ].map(([title, text]) => (
                  <section key={title} className="rounded-xl border p-5">
                    <h2 className="text-2xl">{title}</h2>

                    <p className="mt-3 whitespace-pre-wrap break-words text-sm leading-relaxed text-muted-foreground">
                      {text?.trim() || 'Nie podano informacji.'}
                    </p>
                  </section>
                ))}

                {(profile.marketplace.publicEmail || profile.marketplace.publicPhoneNumber) && (
                  <section className="space-y-3 rounded-xl border p-5">
                    <h2 className="text-2xl">Kontakt do gabinetu</h2>

                    {profile.marketplace.publicPhoneNumber && (
                      <a
                        className="block break-words text-sm text-primary underline"
                        href={`tel:${profile.marketplace.publicPhoneNumber.replace(/[^+0-9]/g, '')}`}
                      >
                        {profile.marketplace.publicPhoneNumber}
                      </a>
                    )}

                    {profile.marketplace.publicEmail && (
                      <a
                        className="block break-words text-sm text-primary underline"
                        href={`mailto:${profile.marketplace.publicEmail}`}
                      >
                        {profile.marketplace.publicEmail}
                      </a>
                    )}
                  </section>
                )}
              </div>

              <p className="text-xs text-muted-foreground">
                Informacje zawodowe zostały podane przez terapeutę. Ich prezentacja nie oznacza niezależnej
                weryfikacji kwalifikacji przez PhysioFlow.
              </p>
            </article>

            {!session.ready ? (
              <LoadingState title="Sprawdzanie sesji…" />
            ) : session.patientId ? (
              <PatientActions
                key={`${session.patientId}:${profile.id}`}
                accountId={session.patientId}
                profile={profile}
              />
            ) : session.loggedIn ? (
              <p className="rounded-xl border bg-card p-5 text-sm text-muted-foreground">
                Prośby o współpracę, ulubieni i rezerwacje są dostępne na koncie pacjenta.
              </p>
            ) : (
              <section className="space-y-4 rounded-2xl border bg-primary/5 p-6">
                <h2 className="text-3xl">Rozpocznij współpracę</h2>

                <p className="text-sm text-muted-foreground">
                  Zaloguj się lub utwórz konto pacjenta. Po akceptacji współpracy przez terapeutę możesz
                  rezerwować wizyty.
                </p>

                <div className="flex flex-wrap gap-3">
                  <Button asChild>
                    <Link
                      href={`/login?${new URLSearchParams({
                        next: path,
                      })}`}
                      onClick={() => rememberMarketplaceReturn(path)}
                    >
                      Zaloguj się
                    </Link>
                  </Button>

                  <Button asChild variant="outline">
                    <Link
                      href={`/register/patient?${new URLSearchParams({
                        next: path,
                      })}`}
                      onClick={() => rememberMarketplaceReturn(path)}
                    >
                      Załóż konto pacjenta
                    </Link>
                  </Button>
                </div>
              </section>
            )}
          </>
        )
      )}
    </section>
  );
}
