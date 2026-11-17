'use client';

import { useEffect, useState } from 'react';
import { BookCheck, History } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { JournalEditor, type JournalTarget } from './JournalEditor';
import { journalHref } from '@/lib/exercise-journal-form';
import { useJournalNavigation } from './JournalNavigation';

export function JournalAction({
  target,
  onEditingChange,
}: {
  target: JournalTarget;
  onEditingChange?: (open: boolean) => void;
}) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    onEditingChange?.(open);
    return () => onEditingChange?.(false);
  }, [open, onEditingChange]);
  return (
    <>
      <Button type="button" className="gap-2" onClick={() => setOpen(true)}>
        <BookCheck size={16} aria-hidden="true" />
        Zapisz wykonanie
      </Button>
      {open && (
        <JournalEditor source={{ target }} onClose={() => setOpen(false)} onSaved={() => setOpen(false)} />
      )}
    </>
  );
}

export function JournalPlanHistory({ planId }: { planId: string }) {
  const router = useRouter();
  const { confirmLeave } = useJournalNavigation();
  return (
    <Button
      type="button"
      variant="outline"
      className="gap-2"
      onClick={() =>
        confirmLeave(() => router.push(journalHref({ from: '', to: '', status: '', therapyPlanId: planId })))
      }
    >
      <History size={16} aria-hidden="true" />
      Historia realizacji planu
    </Button>
  );
}
