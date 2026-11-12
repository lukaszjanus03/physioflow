import { Suspense } from 'react';

import { MarketplaceCatalog } from '@/components/marketplace/Marketplace';
import { LoadingState } from '@/components/ui/loading-state';

export default function TherapistsPage() {
  return (
    <Suspense
      fallback={
        <LoadingState title="Przygotowywanie katalogu…" />
      }
    >
      <MarketplaceCatalog />
    </Suspense>
  );
}