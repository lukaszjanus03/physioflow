import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { Bebas_Neue, Merriweather } from 'next/font/google';

import { Providers } from '@/lib/providers';
import './globals.css';

const merriweather = Merriweather({
  subsets: ['latin', 'latin-ext'],
  style: ['normal', 'italic'],
  display: 'swap',
});

const bebasNeue = Bebas_Neue({
  subsets: ['latin', 'latin-ext'],
  weight: '400',
  display: 'swap',
  variable: '--font-heading',
});

export const metadata: Metadata = {
  title: 'PhysioFlow',
  description: 'Ekosystem cyfrowy dla fizjoterapii',
};

export default function RootLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <html lang="pl" suppressHydrationWarning>
      <body className={`${merriweather.className} ${bebasNeue.variable}`}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}