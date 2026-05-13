'use client';

import { Info, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import type { AnatomicalRegion } from './constants';

interface AnatomicalRegionInfoDialogProps {
  open: boolean;
  region: AnatomicalRegion | null;
  onOpenChange: (open: boolean) => void;
  onAddPainPoint: () => void;
}

export function AnatomicalRegionInfoDialog({
  open,
  region,
  onOpenChange,
  onAddPainPoint,
}: AnatomicalRegionInfoDialogProps) {
  if (!region) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="mb-2 flex h-10 w-10 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Info className="h-5 w-5" />
          </div>
          <DialogTitle>{region.name}</DialogTitle>
          <DialogDescription>Informacje o tym regionie anatomicznym.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4 text-sm">
          <div className="rounded-md border bg-muted/40 p-4">
            <p className="font-medium text-foreground">Opis</p>
            <p className="mt-1 text-muted-foreground">
              Szczegółowe dane o mięśniu lub regionie będą pobierane z backendu po dodaniu endpointu
              <span className="font-mono"> /api/muscles</span>.
            </p>
          </div>

          <div className="rounded-md border bg-background p-4">
            <p className="font-medium text-foreground">Aktualnie dostępne</p>
            <p className="mt-1 text-muted-foreground">
              Możesz użyć tego regionu jako punktu odniesienia i dodać punkt bólu dokładnie w miejscu kliknięcia.
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Zamknij
          </Button>
          <Button onClick={onAddPainPoint}>
            <Plus className="mr-2 h-4 w-4" />
            Dodaj punkt bólu
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
