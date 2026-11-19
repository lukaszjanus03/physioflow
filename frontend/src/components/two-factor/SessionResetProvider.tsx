'use client';

import { useQueryClient } from '@tanstack/react-query';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useState, type ReactNode } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/stores/auth-store';

import type { LoginChallenge, LoginSession } from '@/lib/two-factor-api';
import { TwoFactorFlowContext } from './two-factor-flow-context';
import { TwoFactorLogin } from './TwoFactorLogin';
import { TwoFactorPrompt } from './TwoFactorSettings';

type Screen =
  | { kind: 'codes'; codes: string[] }
  | { kind: 'reauthenticate' }
  | { kind: 'prompt'; accountId: string; continueToApp: () => void }
  | { kind: 'challenge'; challenge: LoginChallenge; complete: (session: LoginSession) => void };

export function SessionResetProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const query = useQueryClient();
  const owner = useAuthStore((state) => (state.isAuthenticated ? state.userId : null));
  const [state, setState] = useState<{ path: string; screen: Screen } | null>(null);
  const [saved, setSaved] = useState(false);
  if (state && state.path !== pathname) {
    setState(null);
    setSaved(false);
  }
  const screen = state?.path === pathname ? state.screen : null;
  const needsLogin = screen?.kind === 'reauthenticate';
  useEffect(() => {
    if (needsLogin && pathname !== '/login') router.replace('/login');
  }, [needsLogin, pathname, router]);
  const setScreen = (next: Screen) => setState({ path: pathname, screen: next });
  const clear = () => setState(null);

  function resetSession(accountId: string, nextCodes?: string[]) {
    const session = useAuthStore.getState();
    if (!session.isAuthenticated || session.userId !== accountId) return;
    if (nextCodes) setScreen({ kind: 'codes', codes: nextCodes });
    else setScreen({ kind: 'reauthenticate' });
    session.logout();
    query.clear();
  }

  return (
    <TwoFactorFlowContext.Provider
      value={{
        resetSession,
        clear,
        showPrompt: (accountId, continueToApp) => setScreen({ kind: 'prompt', accountId, continueToApp }),
        showChallenge: (challenge, complete) => setScreen({ kind: 'challenge', challenge, complete }),
      }}
    >
      {needsLogin && pathname !== '/login' ? (
        <main className="mx-auto max-w-md p-6">
          <p role="status">Przekierowanie do logowania…</p>
        </main>
      ) : screen?.kind === 'codes' && !owner ? (
        <main className="mx-auto max-w-xl p-4 sm:py-12">
          <section className="space-y-5 rounded-2xl border bg-card p-5 shadow-sm sm:p-8">
            <ShieldCheck className="text-primary" aria-hidden="true" />
            <h1 className="text-3xl">Zapisz kody odzyskiwania</h1>
            <p className="text-sm leading-6 text-muted-foreground">
              Każdy kod działa raz. Zapisz je w bezpiecznym miejscu. Po opuszczeniu tego ekranu nie będzie
              można ich ponownie wyświetlić.
            </p>
            <ul aria-label="Kody odzyskiwania" className="space-y-2 rounded-xl border bg-muted/30 p-3">
              {screen.codes.map((code) => (
                <li key={code} className="break-all font-mono text-xs sm:text-sm">
                  {code}
                </li>
              ))}
            </ul>
            <p className="text-sm leading-6 text-muted-foreground">
              Zmiana zabezpieczeń zakończyła sesję. Przy kolejnym logowaniu poczekaj na nowy kod z aplikacji
              lub użyj jednego z kodów odzyskiwania.
            </p>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={saved} onChange={(event) => setSaved(event.target.checked)} />
              Zapisano kody w bezpiecznym miejscu
            </label>
            <Button
              type="button"
              disabled={!saved}
              onClick={() => {
                if (pathname === '/login') clear();
                else setScreen({ kind: 'reauthenticate' });
                setSaved(false);
              }}
            >
              Wróć do logowania
            </Button>
          </section>
        </main>
      ) : screen?.kind === 'challenge' ? (
        <main className="mx-auto max-w-md p-4 sm:py-12">
          <TwoFactorLogin challenge={screen.challenge} complete={screen.complete} back={clear} />
        </main>
      ) : screen?.kind === 'prompt' && owner === screen.accountId ? (
        <main className="mx-auto max-w-md p-4 sm:py-12">
          <TwoFactorPrompt
            accountId={screen.accountId}
            onContinue={() => {
              clear();
              screen.continueToApp();
            }}
          />
        </main>
      ) : (
        children
      )}
    </TwoFactorFlowContext.Provider>
  );
}
