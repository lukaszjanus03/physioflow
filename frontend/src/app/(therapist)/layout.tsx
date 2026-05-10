'use client';

import { Calendar, Dumbbell, LayoutDashboard, Loader2, LogOut, Settings, Users } from 'lucide-react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
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

  useEffect(() => {
    if (!isAuthenticated) router.replace('/');
    else if (!roles.includes('Therapist')) router.replace('/');
  }, [isAuthenticated, roles, router]);

  const handleLogout = async () => {
    setIsLoggingOut(true);

    // Czekamy równolegle na API i minimalny czas pokazu spinnera (UX).
    // Promise.all kończy się, gdy oba są gotowe - nawet jak backend
    // odpowie w 50ms, user zobaczy spinner przez pełne 2s.
    const minDelay = new Promise((resolve) => setTimeout(resolve, LOGOUT_MIN_DURATION_MS));
    const apiCall = authApi.logout().catch(() => {
      // Ignorujemy błąd - i tak chcemy wylogować lokalnie.
    });
    await Promise.all([apiCall, minDelay]);

    toast.success('Do zobaczenia!');
    logout();
    router.replace('/');
  };

  if (!isAuthenticated) return null;

  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-64 flex-col border-r bg-card md:flex">
        <div className="flex h-16 items-center border-b px-6">
          <h1 className="text-xl font-bold">PhysioFlow</h1>
        </div>
        <nav className="flex-1 space-y-1 p-4">
          {navItems.map((item) => {
            const Icon = item.icon;
            const active = pathname.startsWith(item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                  active ? 'bg-primary text-primary-foreground' : 'hover:bg-accent',
                )}
              >
                <Icon className="h-4 w-4" />
                {item.label}
              </Link>
            );
          })}
        </nav>
        <div className="border-t p-4">
          <p className="mb-2 truncate text-xs text-muted-foreground">{email}</p>
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start"
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

      {isLoggingOut && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 backdrop-blur-sm">
          <div className="flex flex-col items-center gap-3 rounded-lg border bg-card p-8 shadow-lg">
            <Loader2 className="h-8 w-8 animate-spin text-primary" />
            <p className="text-sm font-medium">Wylogowywanie...</p>
            <p className="text-xs text-muted-foreground">Za chwilę nastąpi przekierowanie</p>
          </div>
        </div>
      )}

      <main className="flex-1 overflow-y-auto">{children}</main>
    </div>
  );
}