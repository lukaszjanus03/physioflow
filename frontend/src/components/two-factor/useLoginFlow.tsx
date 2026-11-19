'use client';

import { useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '@/stores/auth-store';
import type { LoginResult, LoginSession } from '@/lib/two-factor-api';
import { useTwoFactorFlow } from './two-factor-flow-context';

export function useLoginFlow(continueToApp: () => void) {
  const flow = useTwoFactorFlow();
  const query = useQueryClient();

  function complete(session: LoginSession) {
    query.clear();
    useAuthStore.getState().setTokens(session.accessToken, session.refreshToken);
    const account = useAuthStore.getState();
    if (session.shouldPromptTwoFactor && account.userId && account.accessToken === session.accessToken) {
      flow.showPrompt(account.userId, continueToApp);
    } else {
      flow.clear();
      continueToApp();
    }
  }
  function accept(result: LoginResult) {
    if ('requiresTwoFactor' in result) {
      useAuthStore.getState().logout();
      query.clear();
      flow.showChallenge(result, complete);
    } else complete(result);
  }
  return { accept };
}
