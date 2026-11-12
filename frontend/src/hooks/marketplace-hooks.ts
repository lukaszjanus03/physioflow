'use client';

import { useQuery } from '@tanstack/react-query';
import { useSyncExternalStore } from 'react';

import {
  marketplaceApi,
  marketplaceKeys,
  publicQueryOptions,
} from '@/lib/marketplace-api';
import { useAuthStore } from '@/stores/auth-store';

function subscribe() {
  return () => {};
}

function clientReady() {
  return true;
}

function serverReady() {
  return false;
}

export function useMarketplaceSession() {
  const ready = useSyncExternalStore(
    subscribe,
    clientReady,
    serverReady,
  );

  const {
    isAuthenticated,
    userId,
    roles,
  } = useAuthStore();

  const loggedIn =
    ready &&
    isAuthenticated &&
    Boolean(userId);

  return {
    ready,
    loggedIn,
    patientId:
      loggedIn && roles.includes('Patient')
        ? userId
        : null,
    therapist:
      loggedIn && roles.includes('Therapist'),
  };
}

export function useFavoriteIndex(
  accountId: string | null,
  enabled = true,
) {
  return useQuery({
    queryKey:
      marketplaceKeys.favoriteIndex(accountId ?? ''),
    queryFn: ({ signal }) =>
      marketplaceApi.favoriteIds(signal),
    enabled: Boolean(accountId) && enabled,
    ...publicQueryOptions,
  });
}