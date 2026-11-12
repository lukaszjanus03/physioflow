import type { ReactNode } from 'react';

import { MarketplaceFrame } from '@/components/marketplace/Marketplace';

export default function MarketplaceLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <MarketplaceFrame>
      {children}
    </MarketplaceFrame>
  );
}