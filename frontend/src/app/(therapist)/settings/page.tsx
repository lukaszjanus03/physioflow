'use client';

import {
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';

import TherapistProfileForm from '@/components/therapists/TherapistProfileForm';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { withMinimumLoading } from '@/lib/minimum-loading';
import { getApiMessage } from '@/lib/portal-api';
import {
  profileKeys,
  therapistProfileApi,
} from '@/lib/therapist-profile-api';
import { useAuthStore } from '@/stores/auth-store';

export default function SettingsPage() {
  const {
    userId,
    roles,
    isAuthenticated,
  } = useAuthStore();

  const queryClient = useQueryClient();

  const enabled =
    isAuthenticated &&
    roles.includes('Therapist') &&
    Boolean(userId);

  const query = useQuery({
    queryKey: profileKeys.own(userId ?? ''),
    queryFn: ({ signal, queryKey }) =>
      withMinimumLoading(
        () => therapistProfileApi.get(signal),
        queryClient.getQueryData(queryKey) === undefined,
      ),
    enabled,
    refetchOnWindowFocus: false,
  });

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 sm:p-6">
      <header className="rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-6 sm:p-8">
        <p className="text-xs font-semibold uppercase tracking-widest text-primary">
          Twoja wizytówka
        </p>

        <h1 className="mt-3 text-4xl">
          Profil zawodowy
        </h1>

        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-muted-foreground">
          Przedstaw siebie i swój gabinet. Uzupełnij
          informacje, które pomogą pacjentom wybrać
          terapeutę i przygotować się do wizyty.
        </p>
      </header>

      {!enabled || query.isPending ? (
        <LoadingState title="Wczytywanie profilu…" />
      ) : query.isError ? (
        <section className="space-y-4 rounded-2xl border bg-card p-6">
          <h2 className="text-3xl">
            Nie udało się pobrać profilu
          </h2>

          <p
            role="alert"
            className="text-sm text-destructive"
          >
            {getApiMessage(query.error)}
          </p>

          <Button
            type="button"
            variant="outline"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </section>
      ) : (
        <TherapistProfileForm
          key={`${userId}:${query.data.id}`}
          accountId={userId!}
          profile={query.data}
        />
      )}
    </div>
  );
}