'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/stores/auth-store';

export default function HomePage() {
  const router = useRouter();
  const { isAuthenticated, roles } = useAuthStore();

  useEffect(() => {
    if (!isAuthenticated) {
      router.replace('/login');
    } else if (roles.includes('Therapist')) {
      router.replace('/dashboard');
    } else if (roles.includes('Patient')) {
      router.replace('/portal');
    } else {
      router.replace('/login');
    }
  }, [isAuthenticated, roles, router]);

  return (
    <div className="flex min-h-screen items-center justify-center">
      <p className="text-muted-foreground">Ładowanie...</p>
    </div>
  );
}
