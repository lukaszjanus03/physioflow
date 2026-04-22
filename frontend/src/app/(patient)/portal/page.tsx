'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/stores/auth-store';

export default function PatientPortalPage() {
  const router = useRouter();
  const { isAuthenticated, roles } = useAuthStore();

  useEffect(() => {
    if (!isAuthenticated) router.replace('/login');
    else if (!roles.includes('Patient')) router.replace('/');
  }, [isAuthenticated, roles, router]);

  return (
    <div className="p-6">
      <h1 className="text-3xl font-bold">Portal pacjenta</h1>
      <p className="text-muted-foreground">
        TODO: nadchodzące wizyty, aktualny plan ćwiczeń, dzienniczek, czat z terapeutą
      </p>
    </div>
  );
}
