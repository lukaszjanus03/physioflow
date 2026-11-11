'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  CalendarDays,
  ChevronRight,
  HeartPulse,
  LogOut,
  Menu,
  Search,
  Send,
  User,
  Users,
  X,
} from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  useEffect,
  useRef,
  useState,
  useSyncExternalStore,
} from 'react';

import NotificationBell from '@/components/notifications/NotificationBell';
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
import { LoadingState } from '@/components/ui/loading-state';
import { withMinimumLoading } from '@/lib/minimum-loading';
import { getApiMessage, portalApi } from '@/lib/portal-api';
import { useAuthStore } from '@/stores/auth-store';

function subscribeToClientReady() {
  return () => {};
}

function getClientSnapshot() {
  return true;
}

function getServerSnapshot() {
  return false;
}

type PortalView =
  | 'account'
  | 'appointments'
  | 'catalog'
  | 'requests'
  | 'therapists';

const navigation: {
  id: PortalView;
  label: string;
}[] = [
  { id: 'account', label: 'Moje konto' },
  { id: 'appointments', label: 'Moje wizyty' },
  { id: 'catalog', label: 'Znajdź terapeutę' },
  { id: 'requests', label: 'Moje prośby' },
  { id: 'therapists', label: 'Moi terapeuci' },
];

const navigationIcons = {
  account: User,
  appointments: CalendarDays,
  catalog: Search,
  requests: Send,
  therapists: Users,
};

function PortalNavigation({
  view,
  onNavigate,
  label,
}: {
  view: PortalView;
  onNavigate: (next: PortalView) => void;
  label: string;
}) {
  return (
    <nav aria-label={label} className="space-y-1">
      {navigation.map((item) => {
        const Icon = navigationIcons[item.id];
        const active = view === item.id;

        return (
          <Button
            key={item.id}
            type="button"
            variant="ghost"
            aria-pressed={active}
            onClick={() => onNavigate(item.id)}
            className={`h-11 w-full justify-start gap-3 rounded-md px-3 text-sm font-medium transition-colors ${
              active
                ? 'bg-primary text-primary-foreground shadow-sm hover:bg-primary/90 hover:text-primary-foreground'
                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
            }`}
          >
            <Icon
              aria-hidden="true"
              className="h-4 w-4 shrink-0"
            />
            <span className="truncate">{item.label}</span>
          </Button>
        );
      })}
    </nav>
  );
}

function PortalAccountPanel({
  name,
  email,
  initials,
  onLogout,
}: {
  name: string;
  email?: string | null;
  initials: string;
  onLogout: () => Promise<void>;
}) {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3 rounded-md bg-muted/60 p-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-background text-xs font-bold text-primary shadow-sm">
          {initials}
        </div>

        <div className="min-w-0">
          <p className="truncate text-sm font-medium">
            {name || 'Pacjent'}
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {email || 'Konto pacjenta'}
          </p>
        </div>
      </div>

      <Button
        type="button"
        variant="ghost"
        className="h-10 w-full justify-start gap-3 text-muted-foreground"
        onClick={() => void onLogout()}
      >
        <LogOut
          aria-hidden="true"
          className="h-4 w-4 shrink-0"
        />
        Wyloguj się
      </Button>
    </div>
  );
}

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
    isAuthenticated &&
    roles.includes('Patient') &&
    Boolean(userId);

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
        <p
          role="status"
          className="text-sm text-muted-foreground"
        >
          {isLoggingOut
            ? 'Wylogowywanie…'
            : 'Sprawdzanie sesji…'}
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
  const queryClient = useQueryClient();

  const [view, setView] = useState<PortalView>('account');
  const [appointmentId, setAppointmentId] = useState<string | null>(
    null,
  );
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const mainRef = useRef<HTMLElement>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);

  const query = useQuery({
    queryKey: ['patient-account', accountId],
    queryFn: ({ signal, queryKey }) =>
      withMinimumLoading(
        () => portalApi.me(signal),
        queryClient.getQueryData(queryKey) === undefined,
      ),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  const fullName = [
    query.data?.firstName,
    query.data?.lastName,
  ]
    .filter(Boolean)
    .join(' ');

  const initials =
    `${query.data?.firstName?.trim().charAt(0) ?? ''}${
      query.data?.lastName?.trim().charAt(0) ?? ''
    }`.toUpperCase() || 'PA';

  const activeLabel =
    navigation.find((item) => item.id === view)?.label ??
    'Moje konto';

  function navigate(next: PortalView) {
    setView(next);
    setMobileMenuOpen(false);

    window.requestAnimationFrame(() => {
      mainRef.current?.focus();
    });
  }

  return (
    <div className="min-h-screen bg-muted/30 text-foreground">
      <a
        href="#patient-portal-main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[60] focus:rounded-md focus:bg-background focus:px-4 focus:py-3 focus:shadow-lg"
      >
        Przejdź do treści
      </a>

      <aside className="fixed inset-y-0 left-0 z-40 hidden w-[260px] flex-col border-r bg-card/95 shadow-sm backdrop-blur md:flex">
        <div className="border-b px-5 py-5">
          <Link
            href="/portal"
            onClick={() => navigate('account')}
            className="flex items-center gap-3 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-primary text-primary-foreground shadow-sm">
              <HeartPulse
                aria-hidden="true"
                className="h-5 w-5"
              />
            </span>

            <span className="min-w-0">
              <span className="block text-lg font-bold leading-tight">
                PhysioFlow
              </span>
              <span className="text-xs text-muted-foreground">
                Portal pacjenta
              </span>
            </span>
          </Link>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-3 py-4">
          <p className="mb-3 px-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
            Twoja przestrzeń
          </p>

          <PortalNavigation
            view={view}
            onNavigate={navigate}
            label="Panel pacjenta"
          />
        </div>

        <div className="border-t p-3">
          <PortalAccountPanel
            name={fullName}
            email={query.data?.email}
            initials={initials}
            onLogout={onLogout}
          />
        </div>
      </aside>

      <header className="sticky top-0 z-30 border-b bg-card/95 backdrop-blur md:ml-[260px]">
        <div className="flex min-h-[73px] items-center justify-between gap-3 px-4 sm:px-6 lg:px-8">
          <Link
            href="/portal"
            onClick={() => navigate('account')}
            className="flex items-center gap-3 md:hidden"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-md bg-primary text-primary-foreground">
              <HeartPulse
                aria-hidden="true"
                className="h-5 w-5"
              />
            </span>

            <span>
              <span className="block text-sm font-semibold">
                PhysioFlow
              </span>
              <span className="text-xs text-muted-foreground">
                Portal pacjenta
              </span>
            </span>
          </Link>

          <div className="hidden items-center gap-2 text-sm md:flex">
            <span className="text-muted-foreground">
              Portal pacjenta
            </span>
            <ChevronRight
              aria-hidden="true"
              className="h-4 w-4 text-muted-foreground"
            />
            <span className="font-medium">{activeLabel}</span>
          </div>

          <div className="flex items-center gap-2">
            <NotificationBell role="Patient" />

            <Button
              ref={menuButtonRef}
              type="button"
              variant="outline"
              className="gap-2 md:hidden"
              aria-expanded={mobileMenuOpen}
              aria-controls="patient-mobile-navigation"
              onClick={() =>
                setMobileMenuOpen((current) => !current)
              }
            >
              {mobileMenuOpen ? (
                <X aria-hidden="true" className="h-4 w-4" />
              ) : (
                <Menu aria-hidden="true" className="h-4 w-4" />
              )}

              {mobileMenuOpen ? 'Zamknij' : 'Menu'}
            </Button>
          </div>
        </div>

        <div
          id="patient-mobile-navigation"
          hidden={!mobileMenuOpen}
          className="max-h-[calc(100dvh-73px)] overflow-y-auto border-t p-3 md:hidden"
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault();
              setMobileMenuOpen(false);
              menuButtonRef.current?.focus();
            }
          }}
        >
          <PortalNavigation
            view={view}
            onNavigate={navigate}
            label="Mobilny panel pacjenta"
          />

          <div className="mt-3 border-t pt-3">
            <PortalAccountPanel
              name={fullName}
              email={query.data?.email}
              initials={initials}
              onLogout={onLogout}
            />
          </div>
        </div>
      </header>

      <main
        id="patient-portal-main"
        ref={mainRef}
        tabIndex={-1}
        className="space-y-6 p-4 outline-none sm:p-6 md:ml-[260px] lg:p-8"
      >
        {query.isPending ? (
          <LoadingState
            title="Wczytywanie Twojego konta…"
            description="Pobieramy informacje do portalu pacjenta."
          />
        ) : query.isError ? (
          <section className="space-y-4 rounded-xl border bg-card p-6">
            <h1 className="text-3xl">
              Nie udało się wczytać konta
            </h1>

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
          <>
            {view === 'appointments' && (
              <PatientAppointments
                accountId={accountId}
                onFindTherapist={() => navigate('catalog')}
              />
            )}

            {view === 'catalog' && (
              <TherapistCatalog accountId={accountId} />
            )}

            {view === 'requests' && (
              <MyCooperationRequests accountId={accountId} />
            )}

            {view === 'therapists' && (
              <MyTherapists accountId={accountId} />
            )}

            {view === 'account' && (
              <>
                <section className="rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-6 sm:p-8">
                  <p className="text-xs font-semibold uppercase tracking-widest text-primary">
                    Twoja przestrzeń
                  </p>

                  <h1 className="mt-3 text-4xl sm:text-5xl">
                    Witaj
                    {query.data.firstName
                      ? `, ${query.data.firstName}`
                      : ''}
                  </h1>

                  <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
                    Rezerwuj wizyty, sprawdzaj terminy i zarządzaj
                    współpracą ze swoimi terapeutami.
                  </p>

                  <div className="mt-5 flex flex-wrap gap-3">
                    <Button
                      type="button"
                      onClick={() => navigate('appointments')}
                    >
                      Moje wizyty
                    </Button>

                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => navigate('catalog')}
                    >
                      Znajdź terapeutę
                    </Button>
                  </div>
                </section>

                <PatientNextAppointment
                  accountId={accountId}
                  onDetails={setAppointmentId}
                  onAll={() => navigate('appointments')}
                />

                <section className="rounded-xl border bg-card p-6">
                  <h2 className="text-3xl">Moje konto</h2>

                  <dl className="mt-6 grid gap-5 sm:grid-cols-2">
                    {[
                      [
                        'Imię',
                        query.data.firstName || 'Nie podano',
                      ],
                      [
                        'Nazwisko',
                        query.data.lastName || 'Nie podano',
                      ],
                      ['E-mail', query.data.email],
                      [
                        'Potwierdzenie e-maila',
                        query.data.emailConfirmed
                          ? 'Potwierdzony'
                          : 'Niepotwierdzony',
                      ],
                    ].map(([label, value]) => (
                      <div
                        key={label}
                        className="rounded-xl border p-4"
                      >
                        <dt className="text-xs text-muted-foreground">
                          {label}
                        </dt>
                        <dd className="mt-2 break-words text-sm font-medium">
                          {value}
                        </dd>
                      </div>
                    ))}
                  </dl>
                </section>

                <aside className="rounded-xl border bg-muted/30 p-5 text-sm leading-relaxed text-muted-foreground">
                  Rezerwacje i współpraca nie udostępniają
                  automatycznie wewnętrznych notatek ani
                  dokumentacji medycznej terapeuty.
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