'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { HeartPulse, LogOut } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState, useSyncExternalStore } from 'react';

import PatientAppointments, {
  PatientAppointmentDetails,
  PatientNextAppointment,
} from '@/components/patient-portal/PatientAppointments';
import {
  MyCooperationRequests,
  MyTherapists,
} from '@/components/patient-portal/PatientCooperation';
import TherapistCatalog from '@/components/patient-portal/TherapistCatalog';
import { Button } from '@/components/ui/button';
import { getApiMessage, portalApi } from '@/lib/portal-api';
import { useAuthStore } from '@/stores/auth-store';

function subscribeToClientReady() { return () => {}; }
function getClientSnapshot() { return true; }
function getServerSnapshot() { return false; }

type PortalView =
  | 'account'
  | 'appointments'
  | 'catalog'
  | 'requests'
  | 'therapists';

const navigation: { id: PortalView; label: string }[] = [
  { id: 'account', label: 'Moje konto' },
  { id: 'appointments', label: 'Moje wizyty' },
  { id: 'catalog', label: 'Znajdź terapeutę' },
  { id: 'requests', label: 'Moje prośby' },
  { id: 'therapists', label: 'Moi terapeuci' },
];

export default function PatientPortalPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { isAuthenticated, roles, userId, logout } = useAuthStore();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  const ready = useSyncExternalStore(
    subscribeToClientReady,
    getClientSnapshot,
    getServerSnapshot,
  );

  const allowed =
    isAuthenticated && roles.includes('Patient') && Boolean(userId);

  useEffect(() => {
    if (!ready || allowed || isLoggingOut) return;
    router.replace(isAuthenticated ? '/' : '/login');
  }, [ready, allowed, isAuthenticated, isLoggingOut, router]);

  async function handleLogout() {
    if (isLoggingOut) return;

    setIsLoggingOut(true);

    try {
      await portalApi.logout();
    } catch {
      // Awaria API nie blokuje zakonczenia lokalnej sesji.
    } finally {
      await queryClient.cancelQueries();
      queryClient.clear();
      logout();
      router.replace('/');
    }
  }

  if (isLoggingOut || !ready || !allowed || !userId) {
    return (
      <main className="flex min-h-screen items-center justify-center">
        <p role="status" className="text-sm text-muted-foreground">
          {isLoggingOut ? 'Wylogowywanie…' : 'Sprawdzanie sesji…'}
        </p>
      </main>
    );
  }

  return (
    <PatientPortal
      key={userId}
      accountId={userId}
      onLogout={handleLogout}
    />
  );
}

function PatientPortal({
  accountId,
  onLogout,
}: {
  accountId: string;
  onLogout: () => Promise<void>;
}) {
  const [view, setView] = useState<PortalView>('account');
  const [appointmentId, setAppointmentId] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ['patient-account', accountId],
    queryFn: ({ signal }) => portalApi.me(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b bg-card">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-5 sm:px-6">
          <Link href="/portal" className="flex items-center gap-3">
            <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary text-primary-foreground">
              <HeartPulse aria-hidden="true" className="h-5 w-5" />
            </span>
            <span>
              <span className="block font-semibold">PhysioFlow</span>
              <span className="text-xs text-muted-foreground">Portal pacjenta</span>
            </span>
          </Link>

          <Button type="button" variant="outline" onClick={() => void onLogout()}>
            <LogOut aria-hidden="true" className="mr-2 h-4 w-4" />
            Wyloguj się
          </Button>
        </div>
      </header>

      <main className="mx-auto max-w-6xl space-y-6 px-4 py-8 sm:px-6">
        {query.isPending ? (
          <p role="status">Wczytywanie konta…</p>
        ) : query.isError ? (
          <section className="space-y-4 rounded-xl border bg-card p-6">
            <h1 className="text-3xl">Nie udało się wczytać konta</h1>
            <p role="alert" className="text-sm text-destructive">
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
          <>
            <nav
              aria-label="Portal pacjenta"
              className="flex flex-wrap gap-2 rounded-xl border bg-card p-2"
            >
              {navigation.map((item) => (
                <Button
                  key={item.id}
                  type="button"
                  variant={view === item.id ? 'default' : 'ghost'}
                  aria-pressed={view === item.id}
                  onClick={() => setView(item.id)}
                >
                  {item.label}
                </Button>
              ))}
            </nav>

            {view === 'appointments' && (
              <PatientAppointments
                accountId={accountId}
                onFindTherapist={() => setView('catalog')}
              />
            )}

            {view === 'catalog' && <TherapistCatalog accountId={accountId} />}
            {view === 'requests' && <MyCooperationRequests accountId={accountId} />}
            {view === 'therapists' && <MyTherapists accountId={accountId} />}

            {view === 'account' && (
              <>
                <section className="rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-6 sm:p-8">
                  <p className="text-xs font-semibold uppercase tracking-widest text-primary">
                    Twoja przestrzeń
                  </p>
                  <h1 className="mt-3 text-4xl sm:text-5xl">
                    Witaj{query.data.firstName ? `, ${query.data.firstName}` : ''}
                  </h1>
                  <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
                    Rezerwuj wizyty, sprawdzaj terminy i zarządzaj współpracą
                    ze swoimi terapeutami.
                  </p>
                  <div className="mt-5 flex flex-wrap gap-3">
                    <Button type="button" onClick={() => setView('appointments')}>
                      Moje wizyty
                    </Button>
                    <Button type="button" variant="outline" onClick={() => setView('catalog')}>
                      Znajdź terapeutę
                    </Button>
                  </div>
                </section>

                <PatientNextAppointment
                  accountId={accountId}
                  onDetails={setAppointmentId}
                  onAll={() => setView('appointments')}
                />

                <section className="rounded-xl border bg-card p-6">
                  <h2 className="text-3xl">Moje konto</h2>
                  <dl className="mt-6 grid gap-5 sm:grid-cols-2">
                    {[
                      ['Imię', query.data.firstName || 'Nie podano'],
                      ['Nazwisko', query.data.lastName || 'Nie podano'],
                      ['E-mail', query.data.email],
                      [
                        'Potwierdzenie e-maila',
                        query.data.emailConfirmed ? 'Potwierdzony' : 'Niepotwierdzony',
                      ],
                    ].map(([label, value]) => (
                      <div key={label} className="rounded-xl border p-4">
                        <dt className="text-xs text-muted-foreground">{label}</dt>
                        <dd className="mt-2 break-words text-sm font-medium">{value}</dd>
                      </div>
                    ))}
                  </dl>
                </section>

                <aside className="rounded-xl border bg-muted/30 p-5 text-sm leading-relaxed text-muted-foreground">
                  Rezerwacje i współpraca nie udostępniają automatycznie
                  wewnętrznych notatek ani dokumentacji medycznej terapeuty.
                </aside>
              </>
            )}
          </>
        )}
      </main>

      {appointmentId && (
        <PatientAppointmentDetails
          key={appointmentId}
          accountId={accountId}
          appointmentId={appointmentId}
          onClose={() => setAppointmentId(null)}
        />
      )}
    </div>
  );
}