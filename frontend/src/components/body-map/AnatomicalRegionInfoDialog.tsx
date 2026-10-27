'use client';

import { useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { AlertCircle, Info, Loader2, Plus } from 'lucide-react';

import { musclesApi } from '@/lib/api';
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
  const regionId = region?.id ?? '';

  const {
    data,
    error,
    isPending,
    isError,
    isFetching,
    refetch,
  } = useQuery({
    queryKey: ['anatomical-region-info', regionId],
    queryFn: () => musclesApi.getByRegion(regionId),
    enabled: open && regionId.length > 0,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

  const isNotFound =
    isAxiosError(error) && error.response?.status === 404;

  if (!region) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <div className="mb-2 flex h-10 w-10 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Info className="h-5 w-5" />
          </div>

          <DialogTitle>{region.name}</DialogTitle>

          <DialogDescription>
            Informacje o regionie anatomicznym.
            {' '}
            {region.side === 'Front' ? 'Widok z przodu.' : 'Widok z tyłu.'}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 text-sm" aria-busy={isFetching}>
          {isPending && (
            <div
              className="flex items-center gap-3 rounded-md border bg-muted/40 p-4 text-muted-foreground"
              role="status"
            >
              <Loader2 className="h-5 w-5 shrink-0 animate-spin text-primary" />
              Pobieranie informacji o regionie...
            </div>
          )}

          {isError && (
            <div
              className="rounded-md border bg-muted/40 p-4"
              role="alert"
            >
              <div className="flex items-start gap-3">
                <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-muted-foreground" />

                <div className="space-y-2">
                  <p className="font-medium">
                    {isNotFound
                      ? 'Brak informacji dla tego regionu'
                      : 'Nie udało się pobrać informacji'}
                  </p>

                  <p className="text-muted-foreground">
                    {isNotFound
                      ? 'Ten region nie ma jeszcze opracowanego opisu w bazie.'
                      : 'Sprawdź połączenie i spróbuj ponownie.'}
                    {' '}
                    Nadal możesz dodać punkt bólu.
                  </p>

                  <Button
                    variant="outline"
                    size="sm"
                    disabled={isFetching}
                    onClick={() => void refetch()}
                  >
                    {isFetching && (
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    )}
                    Spróbuj ponownie
                  </Button>
                </div>
              </div>
            </div>
          )}

          {!isPending && !isError && data && (
            <>
              <section className="rounded-md border bg-muted/40 p-4">
                <h3 className="font-medium text-foreground">Opis regionu</h3>

                <p className="mt-2 whitespace-pre-line leading-relaxed text-muted-foreground">
                  {data.description}
                </p>
              </section>

              <section className="rounded-md border bg-background p-4">
                <h3 className="font-medium text-foreground">Funkcja</h3>

                <p className="mt-2 whitespace-pre-line leading-relaxed text-muted-foreground">
                  {data.function}
                </p>
              </section>

              {data.commonIssues.length > 0 && (
                <section className="rounded-md border bg-background p-4">
                  <h3 className="font-medium text-foreground">
                    Możliwe dolegliwości
                  </h3>

                  <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
                    {data.commonIssues.map((issue, index) => (
                      <li key={`${index}-${issue}`}>{issue}</li>
                    ))}
                  </ul>
                </section>
              )}

              {data.examinationTips.length > 0 && (
                <section className="rounded-md border bg-background p-4">
                  <h3 className="font-medium text-foreground">
                    Informacje dotyczące oceny
                  </h3>

                  <ul className="mt-2 list-disc space-y-1 pl-5 text-muted-foreground">
                    {data.examinationTips.map((tip, index) => (
                      <li key={`${index}-${tip}`}>{tip}</li>
                    ))}
                  </ul>
                </section>
              )}

              <p className="text-xs leading-relaxed text-muted-foreground">
                Materiał edukacyjny. Lokalizacja punktu na mapie nie określa
                przyczyny bólu ani rozpoznania.
              </p>
            </>
          )}
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