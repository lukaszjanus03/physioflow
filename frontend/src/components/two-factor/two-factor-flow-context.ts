import { createContext, useContext } from 'react';
import type { LoginChallenge, LoginSession } from '@/lib/two-factor-api';

export const TwoFactorFlowContext = createContext<{
  resetSession: (accountId: string, codes?: string[]) => void;
  showPrompt: (accountId: string, continueToApp: () => void) => void;
  showChallenge: (challenge: LoginChallenge, complete: (session: LoginSession) => void) => void;
  clear: () => void;
}>({ resetSession: () => {}, showPrompt: () => {}, showChallenge: () => {}, clear: () => {} });
export const useTwoFactorFlow = () => useContext(TwoFactorFlowContext);
