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

// ASP.NET Core Identity używa pełnych URI jako nazw claimów (Microsoft convention).
// Tokeny wyglądają tak:
//   "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": "<guid>"
//   "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress": "<email>"
//   "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Therapist"
const CLAIM_NAME_ID = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier';
const CLAIM_EMAIL = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';
const CLAIM_ROLE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

interface JwtPayload {
  sub?: string;
  email?: string;
  role?: string | string[];
  [CLAIM_NAME_ID]?: string;
  [CLAIM_EMAIL]?: string;
  [CLAIM_ROLE]?: string | string[];
  [key: string]: unknown;
}

function parseJwt(token: string): JwtPayload | null {
  try {
    const payload = token.split('.')[1];
    // atob nie radzi sobie z UTF-8 poprawnie, ale dla JWT z ASCII jest OK
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}

function extractClaims(token: string): { userId: string | null; email: string | null; roles: string[] } {
  const c = parseJwt(token);
  if (!c) return { userId: null, email: null, roles: [] };

  // Próbujemy najpierw pełne URI (MS Identity), potem standardowe krótkie nazwy (sub/email/role).
  const userId = c[CLAIM_NAME_ID] ?? c.sub ?? null;
  const email = c[CLAIM_EMAIL] ?? c.email ?? null;
  const rawRole = c[CLAIM_ROLE] ?? c.role;
  const roles = rawRole ? (Array.isArray(rawRole) ? rawRole : [rawRole]) : [];

  return { userId, email, roles };
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
        const { userId, email, roles } = extractClaims(access);
        set({
          accessToken: access,
          refreshToken: refresh,
          userId,
          email,
          roles,
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
