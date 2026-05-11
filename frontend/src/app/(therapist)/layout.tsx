'use client';

import {
  Activity,
  Calendar,
  Dumbbell,
  LayoutDashboard,
  Loader2,
  LogOut,
  Settings,
  Users,
} from 'lucide-react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { authApi } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useAuthStore } from '@/stores/auth-store';

const navItems = [
  { href: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { href: '/patients', label: 'Pacjenci', icon: Users },
  { href: '/calendar', label: 'Kalendarz', icon: Calendar },
  { href: '/exercises', label: 'Ćwiczenia', icon: Dumbbell },
  { href: '/settings', label: 'Ustawienia', icon: Settings },
];

const LOGOUT_MIN_DURATION_MS = 2000;

export default function TherapistLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, roles, email, logout } = useAuthStore();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  const initials = useMemo(() => {
    if (!email) return 'PF';
    return email.slice(0, 2).toUpperCase();
  }, [email]);

  useEffect(() => {
    if (!isAuthenticated) router.replace('/');
    else if (!roles.includes('Therapist')) router.replace('/');
  }, [isAuthenticated, roles, router]);

  const handleLogout = async () => {
    setIsLoggingOut(true);

    const minDelay = new Promise((resolve) => setTimeout(resolve, LOGOUT_MIN_DURATION_MS));
    const apiCall = authApi.logout().catch(() => {
      // Local logout should still happen even if the API request fails.
    });

    await Promise.all([apiCall, minDelay]);

    toast.success('Do zobaczenia!');
    logout();
    router.replace('/');
  };

  if (!isAuthenticated) return null;

  return (
    <div className="min-h-screen bg-muted/30 text-foreground">
      <aside className="fixed inset-y-0 left-0 z-40 hidden w-[260px] flex-col border-r bg-card/95 shadow-sm backdrop-blur md:flex">
        <div className="border-b px-5 py-5">
          <Link href="/dashboard" className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-md bg-primary text-primary-foreground shadow-sm">
              <Activity className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <p className="text-lg font-bold leading-tight">PhysioFlow</p>
              <p className="text-xs text-muted-foreground">Panel terapeuty</p>
            </div>
          </Link>
        </div>

        <nav className="flex-1 space-y-1 px-3 py-4">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href || pathname.startsWith(`${item.href}/`);

            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  'group flex h-11 items-center gap-3 rounded-md px-3 text-sm font-medium transition-colors',
                  active
                    ? 'bg-primary text-primary-foreground shadow-sm'
                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
                )}
              >
                <Icon
                  className={cn(
                    'h-4 w-4 shrink-0',
                    active ? 'text-primary-foreground' : 'text-muted-foreground group-hover:text-accent-foreground',
                  )}
                />
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
            variant="ghost"
            size="sm"
            className="h-10 w-full justify-start text-muted-foreground hover:text-foreground"
            onClick={handleLogout}
            disabled={isLoggingOut}
          >
            {isLoggingOut ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Wylogowywanie...
              </>
            ) : (
              <>
                <LogOut className="mr-2 h-4 w-4" />
                Wyloguj
              </>
            )}
          </Button>
        </div>
      </aside>

      <main className="min-h-screen pb-20 md:ml-[260px] md:pb-0">{children}</main>

      <nav className="fixed inset-x-0 bottom-0 z-40 border-t bg-card/95 px-2 py-2 shadow-lg backdrop-blur md:hidden">
        <div className="grid grid-cols-5 gap-1">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href || pathname.startsWith(`${item.href}/`);

            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  'flex min-h-12 flex-col items-center justify-center rounded-md px-1 text-[11px] font-medium transition-colors',
                  active ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
                )}
              >
                <Icon className="mb-1 h-4 w-4" />
                <span className="max-w-full truncate">{item.label}</span>
              </Link>
            );
          })}
        </div>
      </nav>

      {isLoggingOut && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm">
          <div className="flex flex-col items-center gap-3 rounded-lg border bg-card p-8 shadow-lg">
            <Loader2 className="h-8 w-8 animate-spin text-primary" />
            <p className="text-sm font-medium">Wylogowywanie...</p>
            <p className="text-xs text-muted-foreground">Za chwilę nastąpi przekierowanie</p>
          </div>
        </div>
      )}
    </div>
  );
}
