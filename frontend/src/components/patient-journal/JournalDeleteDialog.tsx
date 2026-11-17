'use client';

import { useEffect, useRef, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { journalApi, journalError, journalHttpStatus, type JournalEntry } from '@/lib/exercise-journal-api';
import { JournalReportView } from './JournalEntryView';
import { useJournalNavigation } from './JournalNavigation';

export function JournalDeleteDialog({
  entry,
  onClose,
  onDeleted,
}: {
  entry: JournalEntry;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [current, setCurrent] = useState(entry);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [needsRead, setNeedsRead] = useState(false);
  const request = useRef<AbortController | null>(null);
  const { setDraft } = useJournalNavigation();
  useEffect(() => {
    setDraft({ dirty: false, busy });
    return () => setDraft({ dirty: false, busy: false });
  }, [busy, setDraft]);
  useEffect(() => () => request.current?.abort(), []);

  async function act(read: boolean) {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    try {
      if (read) {
        const fresh = await journalApi.get(current.id, controller.signal);
        if (controller.signal.aborted) return;
        setCurrent(fresh);
        setNeedsRead(false);
        setMessage(
          'Pobrano aktualny wpis. Sprawdź treść i ponownie potwierdź, jeśli nadal chcesz go usunąć.',
        );
      } else {
        await journalApi.remove(current.id, current.version, controller.signal);
        if (controller.signal.aborted) return;
        toast.success('Wpis usunięty z dziennika.');
        onDeleted();
      }
    } catch (error) {
      if (controller.signal.aborted) return;
      if (journalHttpStatus(error) === 404) {
        toast.info('Wpis nie jest już dostępny. Odświeżono listę.');
        onDeleted();
      } else {
        setMessage(journalError(error));
        setNeedsRead(true);
      }
    } finally {
      request.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) onClose();
      }}
    >
      <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-lg overflow-y-auto rounded-2xl">
        <DialogHeader>
          <DialogTitle>Usunąć wpis dziennika?</DialogTitle>
          <DialogDescription>
            Wpis zniknie z Twojej historii. Usunięcie nie zmienia planu ani zaleceń terapeuty.
          </DialogDescription>
        </DialogHeader>
        <p className="font-semibold [overflow-wrap:anywhere]">{current.exercise.name}</p>
        <JournalReportView entry={current} />
        {message && (
          <p role="alert" className="text-sm leading-6 text-destructive">
            {message}
          </p>
        )}
        <div className="flex flex-wrap justify-end gap-3">
          <Button variant="outline" disabled={busy} onClick={onClose}>
            Anuluj
          </Button>
          {needsRead ? (
            <Button disabled={busy} onClick={() => void act(true)}>
              Pobierz aktualny wpis
            </Button>
          ) : (
            <Button variant="destructive" disabled={busy} onClick={() => void act(false)}>
              {busy ? 'Usuwanie…' : 'Potwierdzam usunięcie'}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
