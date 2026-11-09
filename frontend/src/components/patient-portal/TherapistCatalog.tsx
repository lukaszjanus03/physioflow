'use client';

import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, MapPin, Search } from 'lucide-react';
import { useState } from 'react';

import { SendCooperationRequest } from '@/components/patient-portal/PatientCooperation';
import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import { getApiMessage, getApiStatus, portalApi } from '@/lib/portal-api';

type Props = {
  accountId: string;
};

export default function TherapistCatalog({ accountId }: Props) {
  const [draft, setDraft] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ['patient-catalog', accountId, search, page],
    queryFn: ({ signal }) => portalApi.searchTherapists(search, page, signal),
    enabled: selectedId === null,
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  if (selectedId) {
    return (
      <TherapistDetails
        key={selectedId}
        accountId={accountId}
        therapistId={selectedId}
        onBack={() => setSelectedId(null)}
      />
    );
  }

  return (
    <section className="space-y-6">
      <div>
        <h1 className="text-4xl">Znajdź terapeutę</h1>
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          Poznaj terapeutów korzystających z PhysioFlow i wyślij prośbę
          o rozpoczęcie współpracy.
        </p>
      </div>

      <form
        className="rounded-xl border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();

          const nextSearch = draft.trim();

          if (nextSearch === search && page === 1) {
            void query.refetch();
          }

          setSearch(nextSearch);
          setPage(1);
        }}
      >
        <Label htmlFor="catalog-search">
          Imię, nazwisko, miasto lub nazwa gabinetu
        </Label>

        <div className="mt-2 flex flex-col gap-3 sm:flex-row">
          <Input
            id="catalog-search"
            type="search"
            maxLength={100}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder="Np. Anna Kraków"
          />

          <Button type="submit" disabled={query.isFetching}>
            <Search aria-hidden="true" className="mr-2 h-4 w-4" />
            Szukaj
          </Button>

          {(search || draft) && (
            <Button
              type="button"
              variant="outline"
              onClick={() => {
                setDraft('');
                setSearch('');
                setPage(1);
              }}
            >
              Wyczyść
            </Button>
          )}
        </div>
      </form>

      {query.isPending ? (
        <p role="status">Wczytywanie katalogu…</p>
      ) : query.isError ? (
        <div role="alert" className="rounded-xl border bg-card p-6">
          <p className="text-sm text-destructive">
            {getApiMessage(query.error)}
          </p>
          <Button
            type="button"
            variant="outline"
            className="mt-4"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </div>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {query.isFetching
              ? 'Odświeżanie…'
              : `Znalezione profile: ${query.data.totalCount}`}
          </p>

          {query.data.items.length === 0 ? (
            <div className="rounded-xl border border-dashed p-8 text-center">
              <h2 className="text-2xl">Brak profili do wyświetlenia</h2>
              <p className="mt-3 text-sm text-muted-foreground">
                {search
                  ? 'Zmień wyszukiwaną frazę lub wyczyść filtr.'
                  : 'Profile pojawią się po opublikowaniu ich przez terapeutów.'}
              </p>
            </div>
          ) : (
            <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {query.data.items.map((therapist) => (
                <article
                  key={therapist.id}
                  className="flex flex-col rounded-xl border bg-card p-5"
                >
                  <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 font-semibold text-primary">
                    {therapist.firstName.slice(0, 1)}
                    {therapist.lastName.slice(0, 1)}
                  </div>

                  <h2 className="text-2xl">
                    {therapist.firstName} {therapist.lastName}
                  </h2>

                  <p className="mt-2 break-words text-sm font-medium">
                    {therapist.practiceName}
                  </p>

                  <p className="mt-2 flex items-center gap-2 text-sm text-muted-foreground">
                    <MapPin aria-hidden="true" className="h-4 w-4 shrink-0" />
                    {therapist.city}
                  </p>

                  <p className="my-5 flex-1 break-words text-sm leading-relaxed text-muted-foreground">
                    {therapist.bio || 'Terapeuta nie dodał jeszcze opisu.'}
                  </p>

                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setSelectedId(therapist.id)}
                  >
                    Zobacz profil
                  </Button>
                </article>
              ))}
            </div>
          )}

          {query.data.totalPages > 1 && (
            <nav
              aria-label="Strony katalogu"
              className="flex flex-wrap items-center justify-between gap-3"
            >
              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasPrevious || query.isFetching}
                onClick={() => setPage((current) => current - 1)}
              >
                Poprzednia
              </Button>

              <span className="text-sm text-muted-foreground">
                Strona {query.data.pageNumber} z {query.data.totalPages}
              </span>

              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasNext || query.isFetching}
                onClick={() => setPage((current) => current + 1)}
              >
                Następna
              </Button>
            </nav>
          )}
        </>
      )}
    </section>
  );
}

function TherapistDetails({
  accountId,
  therapistId,
  onBack,
}: {
  accountId: string;
  therapistId: string;
  onBack: () => void;
}) {
  const query = useQuery({
    queryKey: ['patient-therapist', accountId, therapistId],
    queryFn: ({ signal }) => portalApi.therapist(therapistId, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  return (
    <section className="space-y-6">
      <Button type="button" variant="outline" onClick={onBack}>
        <ArrowLeft aria-hidden="true" className="mr-2 h-4 w-4" />
        Wróć do wyników
      </Button>

      {query.isPending ? (
        <p role="status">Wczytywanie profilu…</p>
      ) : query.isError ? (
        <div role="alert" className="rounded-xl border bg-card p-6">
          <h1 className="text-3xl">Profil niedostępny</h1>

          <p className="mt-3 text-sm text-muted-foreground">
            {getApiStatus(query.error) === 404
              ? 'Profil nie istnieje lub nie jest już opublikowany.'
              : getApiMessage(query.error)}
          </p>

          {getApiStatus(query.error) !== 404 && (
            <Button
              type="button"
              variant="outline"
              className="mt-4"
              disabled={query.isFetching}
              onClick={() => void query.refetch()}
            >
              Spróbuj ponownie
            </Button>
          )}
        </div>
      ) : (
        <article className="rounded-2xl border bg-card p-6 sm:p-8">
          <span className="text-xs font-semibold uppercase tracking-widest text-primary">
            Profil zawodowy
          </span>

          <h1 className="mt-3 text-4xl">
            {query.data.firstName} {query.data.lastName}
          </h1>

          <p className="mt-3 break-words font-medium">
            {query.data.practiceName}
          </p>
          <p className="mt-2 text-sm text-muted-foreground">
            {query.data.city}
          </p>

          <div className="mt-6 border-t pt-6">
            <h2 className="text-2xl">O terapeucie</h2>

            <p className="mt-3 whitespace-pre-wrap break-words text-sm leading-relaxed text-muted-foreground">
              {query.data.bio || 'Terapeuta nie dodał jeszcze opisu.'}
            </p>
          </div>

          <SendCooperationRequest
            accountId={accountId}
            therapistId={therapistId}
            therapistName={`${query.data.firstName} ${query.data.lastName}`}
          />
        </article>
      )}
    </section>
  );
}