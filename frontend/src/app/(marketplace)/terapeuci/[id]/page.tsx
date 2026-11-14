import { Suspense } from 'react';

import { MarketplaceProfile } from '@/components/marketplace/Marketplace';
import TherapistReviews from '@/components/marketplace/TherapistReviews';
import { LoadingState } from '@/components/ui/loading-state';

export default async function TherapistPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  return (
    <>
      <Suspense
        fallback={
          <LoadingState title="Przygotowywanie profilu…" />
        }
      >
        <MarketplaceProfile
          key={id}
          therapistId={id}
        />
      </Suspense>

      <TherapistReviews
        key={`reviews:${id}`}
        therapistId={id}
      />
    </>
  );
}