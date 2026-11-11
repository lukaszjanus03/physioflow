'use client';

import { useQueryClient } from '@tanstack/react-query';
import {
  Activity,
  Calendar,
  Dumbbell,
  LayoutDashboard,
  Loader2,
  LogOut,
  Settings,
  UserPlus,
  Users,
} from 'lucide-react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/button';
import { portalApi } from '@/lib/portal-api';
import { cn } from '@/lib/utils';
import { useAuthStore } from '@/stores/auth-store';
import NotificationBell from '@/components/notifications/NotificationBell';

const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/patients', label: 'Pacjenci', icon: Users },
  {
    href: '/cooperation-requests',
    label: 'Prośby pacjentów',
    icon: UserPlus,
  },
  { href: '/calendar', label: 'Kalendarz', icon: Calendar },
  { href: '/exercises', label: 'Ćwiczenia', icon: Dumbbell },
  { href: '/settings', label: 'Ustawienia', icon: Settings },
];

const LOGOUT_MIN_DURATION_MS = 2000;

export default function TherapistLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const { isAuthenticated, roles, email, logout } = useAuthStore();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  const initials = useMemo(() => (email ? email.slice(0, 2).toUpperCase() : 'PF'), [email]);

  const allowed = isAuthenticated && roles.includes('Therapist');

  useEffect(() => {
    if (!allowed && !isLoggingOut) router.replace('/');
  }, [allowed, isLoggingOut, router]);

  async function handleLogout() {
    if (isLoggingOut) return;

    setIsLoggingOut(true);

    const minDelay = new Promise((resolve) => setTimeout(resolve, LOGOUT_MIN_DURATION_MS));

    await Promise.all([
      portalApi.logout().catch(() => {
        // Awaria API nie blokuje zakonczenia lokalnej sesji.
      }),
      minDelay,
    ]);

    await queryClient.cancelQueries();
    queryClient.clear();
    logout();
    toast.success('Do zobaczenia!');
    router.replace('/');
  }

  if (!allowed) return null;

  return (
    <div className="min-h-screen bg-muted/30 text-foreground">
      <aside className="fixed inset-y-0 left-0 z-40 hidden w-[260px] flex-col border-r bg-card/95 shadow-sm backdrop-blur md:flex">
        <div className="border-b px-5 py-5">
          <Link href="/dashboard" className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-md bg-primary text-primary-foreground shadow-sm">
              <Activity aria-hidden="true" className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <p className="text-lg font-bold leading-tight">PhysioFlow</p>
              <p className="text-xs text-muted-foreground">Panel terapeuty</p>
            </div>
          </Link>
        </div>

        <nav aria-label="Panel terapeuty" className="flex-1 space-y-1 px-3 py-4">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href || pathname.startsWith(`${item.href}/`);

            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'group flex h-11 items-center gap-3 rounded-md px-3 text-sm font-medium transition-colors',
                  active
                    ? 'bg-primary text-primary-foreground shadow-sm'
                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
                )}
              >
                <Icon aria-hidden="true" className="h-4 w-4 shrink-0" />
                <span className="truncate">{item.label}</span>
              </Link>
            );
          })}
        </nav>

        <div className="border-t p-3">
          <div className="mb-3 flex items-center gap-3 rounded-md bg-muted/60 p-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-background text-xs font-bold text-primary shadow-sm">
              {initials}
            </div>
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">Terapeuta</p>
              <p className="truncate text-xs text-muted-foreground">{email}</p>
            </div>
          </div>

          <Button
            type="button"
            variant="ghost"
            className="h-10 w-full justify-start text-muted-foreground"
            onClick={() => void handleLogout()}
            disabled={isLoggingOut}
          >
            {isLoggingOut ? (
              <Loader2 aria-hidden="true" className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <LogOut aria-hidden="true" className="mr-2 h-4 w-4" />
            )}
            {isLoggingOut ? 'Wylogowywanie…' : 'Wyloguj'}
          </Button>
        </div>
      </aside>

      <main className="min-h-screen pb-24 md:ml-[260px] md:pb-0">
        <header className="sticky top-0 z-30 flex min-h-[73px] items-center justify-between gap-3 border-b bg-card/95 px-4 backdrop-blur sm:px-6 lg:px-8">
          <span className="text-sm font-medium">Panel terapeuty</span>

          <NotificationBell role="Therapist" />
        </header>

        {children}
      </main>

      <nav
        aria-label="Mobilny panel terapeuty"
        className="fixed inset-x-0 bottom-0 z-40 overflow-x-auto border-t bg-card/95 p-2 shadow-lg backdrop-blur md:hidden"
      >
        <div className="grid min-w-[420px] grid-cols-6 gap-1">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href || pathname.startsWith(`${item.href}/`);

            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'flex min-h-14 flex-col items-center justify-center rounded-md px-1 text-center text-[11px] font-medium transition-colors',
                  active ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
                )}
              >
                <Icon aria-hidden="true" className="mb-1 h-4 w-4" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </div>
      </nav>

      {isLoggingOut && (
        <div
          role="status"
          className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm"
        >
          <div className="flex flex-col items-center gap-3 rounded-lg border bg-card p-8 shadow-lg">
            <Loader2 aria-hidden="true" className="h-8 w-8 animate-spin text-primary" />
            <p className="text-sm font-medium">Wylogowywanie…</p>
            <p className="text-xs text-muted-foreground">Za chwilę nastąpi przekierowanie</p>
          </div>
        </div>
      )}
    </div>
  );
}
