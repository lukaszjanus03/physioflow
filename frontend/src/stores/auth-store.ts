import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  userId: string | null;
  email: string | null;
  roles: string[];
  isAuthenticated: boolean;
  setTokens: (access: string, refresh: string) => void;
  setUser: (userId: string, email: string, roles: string[]) => void;
  logout: () => void;
}

// Microsoft Identity wystawia claimy jako pełne URI - mapujemy je na krótkie nazwy.
const CLAIM_NAME_ID = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier';
const CLAIM_EMAIL = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';
const CLAIM_ROLE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

interface JwtClaims {
  sub?: string;
  email?: string;
  role?: string | string[];
  nameid?: string;
  [key: string]: unknown;
}

function parseJwt(token: string): JwtClaims | null {
  try {
    const payload = token.split('.')[1];
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
  } catch {
    return null;
  }
}

function extractUserId(claims: JwtClaims): string | null {
  return (claims.sub ?? claims[CLAIM_NAME_ID] ?? claims.nameid ?? null) as string | null;
}

function extractEmail(claims: JwtClaims): string | null {
  return (claims.email ?? claims[CLAIM_EMAIL] ?? null) as string | null;
}

function extractRoles(claims: JwtClaims): string[] {
  const roleClaim = claims.role ?? claims[CLAIM_ROLE];
  if (!roleClaim) return [];
  return Array.isArray(roleClaim) ? roleClaim : [roleClaim as string];
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      userId: null,
      email: null,
      roles: [],
      isAuthenticated: false,
      setTokens: (access, refresh) => {
        const claims = parseJwt(access);
        if (!claims) return;
        set({
          accessToken: access,
          refreshToken: refresh,
          userId: extractUserId(claims),
          email: extractEmail(claims),
          roles: extractRoles(claims),
          isAuthenticated: true,
        });
      },
      setUser: (userId, email, roles) =>
        set({ userId, email, roles, isAuthenticated: true }),
      logout: () =>
        set({
          accessToken: null,
          refreshToken: null,
          userId: null,
          email: null,
          roles: [],
          isAuthenticated: false,
        }),
    }),
    {
      name: 'physioflow-auth',
      partialize: (s) => ({
        accessToken: s.accessToken,
        refreshToken: s.refreshToken,
        userId: s.userId,
        email: s.email,
        roles: s.roles,
        isAuthenticated: s.isAuthenticated,
      }),
    },
  ),
);