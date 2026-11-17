'use client';

import { createContext, useContext, useState, type ReactNode } from 'react';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';

type DraftState = { dirty: boolean; busy: boolean };
const Navigation = createContext({
  setDraft: (_state: DraftState) => {},
  confirmLeave: (run: () => void) => run(),
});

export function JournalNavigationProvider({ children }: { children: ReactNode }) {
  const [draft, setDraft] = useState<DraftState>({ dirty: false, busy: false });
  const { confirmLeave, discardDialog } = useUnsavedChanges(draft.dirty, draft.busy);
  return (
    <Navigation.Provider value={{ setDraft, confirmLeave }}>
      {children}
      {discardDialog}
    </Navigation.Provider>
  );
}
export const useJournalNavigation = () => useContext(Navigation);
