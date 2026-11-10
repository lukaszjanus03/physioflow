'use client';

import { CalendarDays, UserRound } from 'lucide-react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

const items = [
  {
    href: '/settings',
    label: 'Profil zawodowy',
    icon: UserRound,
  },
  {
    href: '/settings/calendar',
    label: 'Kalendarz i wizyty',
    icon: CalendarDays,
  },
];

export default function SettingsLayout({
  children,
}: {
  children: ReactNode;
}) {
  const pathname = usePathname();

  return (
    <div>
      <div className="mx-auto max-w-5xl px-4 pt-6 sm:px-6">
        <nav
          aria-label="Sekcje ustawień"
          className="flex flex-wrap gap-2 rounded-xl border bg-card p-2"
        >
          {items.map((item) => {
            const Icon = item.icon;
            const active = pathname === item.href;

            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? 'page' : undefined}
                className={cn(
                  'flex items-center gap-2 rounded-lg px-4 py-3 text-sm font-medium transition-colors',
                  active
                    ? 'bg-primary text-primary-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                )}
              >
                <Icon aria-hidden="true" className="h-4 w-4" />
                {item.label}
              </Link>
            );
          })}
        </nav>
      </div>

      {children}
    </div>
  );
}