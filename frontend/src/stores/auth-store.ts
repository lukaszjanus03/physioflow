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

// Parsowanie JWT żeby wyjąć claimy bez dodatkowej zależności (jose/jwt-decode)
function parseJwt(token: string): { sub?: string; email?: string; role?: string | string[] } | null {
  try {
    const payload = token.split('.')[1];
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
  } catch {
    return null;
  }
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
        const roles = claims?.role
          ? Array.isArray(claims.role) ? claims.role : [claims.role]
          : [];
        set({
          accessToken: access,
          refreshToken: refresh,
          userId: claims?.sub ?? null,
          email: claims?.email ?? null,
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
