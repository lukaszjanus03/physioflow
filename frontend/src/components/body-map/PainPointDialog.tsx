'use client';

import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/primitives';
import { Textarea } from '@/components/ui/textarea';
import type { PainSeverity } from '@/types/api';
import { ALL_SEVERITIES, SEVERITY_COLORS, SEVERITY_DESCRIPTIONS, SEVERITY_LABELS } from './constants';

interface PainPointDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialSeverity?: PainSeverity;
  initialPainScale?: number | null;
  initialNote?: string | null;
  onDelete?: () => void;
  onSave: (severity: PainSeverity, note: string | null, painScale: number) => void;
}

export function PainPointDialog({
  open,
  onOpenChange,
  initialSeverity = 'AcutePain',
  initialPainScale = 5,
  initialNote = null,
  onDelete,
  onSave,
}: PainPointDialogProps) {
  const [severity, setSeverity] = useState<PainSeverity>(initialSeverity);
  const [painScale, setPainScale] = useState(initialPainScale ?? 5);
  const [note, setNote] = useState(initialNote ?? '');

  useEffect(() => {
    if (open) {
      setSeverity(initialSeverity);
      setPainScale(initialPainScale ?? 5);
      setNote(initialNote ?? '');
    }
  }, [open, initialSeverity, initialPainScale, initialNote]);

  const isEdit = onDelete !== undefined;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? 'Edytuj punkt bólu' : 'Nowy punkt bólu'}</DialogTitle>
          <DialogDescription>
            Wybierz charakter bólu, skalę 1-10 i dodaj ewentualne uwagi.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-5">
          <div className="space-y-2">
            <Label>Charakter bólu</Label>

            <div className="grid grid-cols-1 gap-2">
              {ALL_SEVERITIES.map((s) => (
                <button
                  key={s}
                  type="button"
                  onClick={() => setSeverity(s)}
                  className={`flex items-center gap-3 rounded-md border p-3 text-left transition-colors ${
                    severity === s
                      ? 'border-primary bg-primary/5'
                      : 'border-border hover:bg-accent'
                  }`}
                >
                  <div
                    className="h-6 w-6 flex-shrink-0 rounded-full border-2 border-white shadow"
                    style={{ backgroundColor: SEVERITY_COLORS[s] }}
                  />

                  <div className="flex-1">
                    <div className="font-medium">{SEVERITY_LABELS[s]}</div>
                    <div className="text-xs text-muted-foreground">{SEVERITY_DESCRIPTIONS[s]}</div>
                  </div>
                </button>
              ))}
            </div>
          </div>

          <div className="space-y-3 rounded-lg border bg-muted/30 p-4">
            <div className="flex items-center justify-between gap-3">
              <div>
                <Label htmlFor="painScale">Skala bólu</Label>
                <p className="mt-1 text-xs text-muted-foreground">
                  1 oznacza minimalny ból, 10 bardzo silny ból.
                </p>
              </div>

              <div className="rounded-lg bg-background px-3 py-2 text-xl font-semibold">
                {painScale}/10
              </div>
            </div>

            <input
              id="painScale"
              type="range"
              min={1}
              max={10}
              step={1}
              value={painScale}
              onChange={(event) => setPainScale(Number(event.target.value))}
              className="w-full accent-primary"
            />

            <div className="grid grid-cols-10 gap-1">
              {Array.from({ length: 10 }, (_, index) => index + 1).map((value) => (
                <button
                  key={value}
                  type="button"
                  onClick={() => setPainScale(value)}
                  className={`rounded-md border py-1 text-xs font-medium transition ${
                    painScale === value
                      ? 'border-primary bg-primary text-primary-foreground'
                      : 'bg-background hover:bg-accent'
                  }`}
                >
                  {value}
                </button>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="note">Notatka (opcjonalnie)</Label>

            <Textarea
              id="note"
              placeholder="Np. Pojawia się przy skłonach w prawo"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={500}
            />
          </div>
        </div>

        <DialogFooter className="gap-2 sm:gap-2">
          {isEdit && (
            <Button variant="destructive" onClick={onDelete} className="mr-auto">
              Usuń punkt
            </Button>
          )}

          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Anuluj
          </Button>

          <Button onClick={() => onSave(severity, note.trim() || null, painScale)}>
            {isEdit ? 'Zapisz' : 'Dodaj'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
