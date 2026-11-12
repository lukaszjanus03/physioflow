'use client';

import { Suspense } from 'react';

import { MarketplaceCatalog } from '@/components/marketplace/Marketplace';
import { LoadingState } from '@/components/ui/loading-state';

export default function TherapistCatalog({
  accountId,
}: {
  accountId: string;
}) {
  return (
    <Suspense
      fallback={
        <LoadingState title="Wczytywanie katalogu…" />
      }
    >
      <MarketplaceCatalog
        key={accountId}
        embedded
      />
    </Suspense>
  );
}