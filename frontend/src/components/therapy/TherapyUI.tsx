'use client';

import { useId } from 'react';
import ExerciseVideoPlayer from '@/components/therapy/ExerciseVideoPlayer';

import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/primitives';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { planLabels, therapyDate, therapyError } from '@/lib/therapy-api';
import type { PlanContent, PlanStatus } from '@/lib/therapy-api';

export function TherapyError({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <div className="space-y-3 rounded-xl border border-destructive/30 bg-card p-4">
      <p role="alert" className="text-sm text-destructive">
        {therapyError(error)}
      </p>
      {retry && (
        <Button type="button" variant="outline" onClick={retry}>
          Pobierz aktualne dane
        </Button>
      )}
    </div>
  );
}

export function Field({
  label,
  value,
  onChange,
  max,
  error,
  multiline = false,
  type = 'text',
  disabled = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  max?: number;
  error?: string;
  multiline?: boolean;
  type?: string;
  disabled?: boolean;
}) {
  const id = useId();

  const shared = {
    id,
    value,
    disabled,
    'aria-invalid': Boolean(error),
    'aria-describedby': `${id}-help`,
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
      onChange(event.target.value),
  };

  return (
    <div className="space-y-2">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>

      {multiline ? (
        <Textarea {...shared} rows={5} maxLength={max} />
      ) : (
        <Input
          {...shared}
          type={type}
          maxLength={type === 'number' ? undefined : max}
          step={type === 'number' ? 1 : undefined}
        />
      )}

      <p
        id={`${id}-help`}
        className={`text-xs ${error ? 'text-destructive' : 'text-muted-foreground'}`}
      >
        {error || (max ? `${value.length}/${max}` : '')}
      </p>
    </div>
  );
}

export function Pages({
  page,
  pages,
  next,
  busy,
  onChange,
}: {
  page: number;
  pages: number;
  next: boolean;
  busy: boolean;
  onChange: (page: number) => void;
}) {
  return (
    <nav aria-label="Strony wyników" className="flex flex-wrap items-center justify-between gap-3">
      <Button
        type="button"
        variant="outline"
        disabled={page <= 1 || busy}
        onClick={() => onChange(page - 1)}
      >
        Poprzednia
      </Button>

      <span className="text-sm">
        {page} / {Math.max(1, pages)}
      </span>

      <Button
        type="button"
        variant="outline"
        disabled={!next || busy}
        onClick={() => onChange(page + 1)}
      >
        Następna
      </Button>
    </nav>
  );
}

export function StatusBadge({ status }: { status: PlanStatus }) {
  return (
    <span
      className={`rounded-full px-3 py-1 text-xs font-medium ${
        status === 'Active'
          ? 'bg-emerald-50 text-emerald-800'
          : status === 'Draft'
            ? 'bg-amber-50 text-amber-800'
            : 'bg-muted text-muted-foreground'
      }`}
    >
      {planLabels[status]}
    </span>
  );
}

export function Confirm({
  title,
  text,
  busy,
  onClose,
  onConfirm,
}: {
  title: string;
  text: string;
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busy) onClose();
      }}
    >
      <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] overflow-y-auto rounded-2xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{text}</DialogDescription>
        </DialogHeader>

        <DialogFooter>
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>
            Anuluj
          </Button>
          <Button type="button" disabled={busy} onClick={onConfirm}>
            {busy ? 'Zapisywanie…' : 'Potwierdź'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function PlanView({ content }: { content: PlanContent }) {
  return (
    <div className="space-y-5">
      <section className="space-y-3 rounded-2xl border bg-card p-5">
        <h2 className="text-3xl">{content.name || 'Plan bez nazwy'}</h2>

        <p className="text-sm text-muted-foreground">
          {therapyDate(content.startDate)} – {therapyDate(content.endDate)}
        </p>

        <h3 className="text-xl">Cel terapii</h3>
        <p className="whitespace-pre-wrap break-words text-sm leading-relaxed">
          {content.goal || 'Nie uzupełniono.'}
        </p>

        <h3 className="text-xl">Zalecenia ogólne</h3>
        <p className="whitespace-pre-wrap break-words text-sm leading-relaxed">
          {content.generalRecommendations || 'Nie uzupełniono.'}
        </p>
      </section>

      {!content.exercises.length && <p>Nie dodano ćwiczeń.</p>}

      <div className="grid items-start gap-4 lg:grid-cols-2">
        {content.exercises.map((item, index) => (
          <article key={item.id} className="min-w-0 space-y-4 rounded-2xl border bg-card p-5">
            <h3 className="text-2xl">
              {index + 1}. {item.name || 'Ćwiczenie bez nazwy'}
            </h3>

            <p className="whitespace-pre-wrap break-words text-sm leading-7">
              {item.instructions || 'Brak instrukcji w zapisanej kopii.'}
            </p>

            <ExerciseVideoPlayer video={item.video} title={item.name || 'Ćwiczenie'} />
            {item.video === undefined && (
              <p className="text-xs leading-relaxed text-muted-foreground">
                Materiał nowej pozycji zostanie ustalony z biblioteki podczas zapisu. Po zapisie
                plan pokaże utrwaloną referencję do filmu.
              </p>
            )}
            <div className="flex flex-wrap gap-2 text-sm">
              <span className="rounded-lg bg-primary/10 px-3 py-2">Serie: {item.sets ?? '—'}</span>

              {item.repetitions !== null && (
                <span className="rounded-lg bg-muted px-3 py-2">
                  Powtórzenia: {item.repetitions}
                </span>
              )}

              {item.durationSeconds !== null && (
                <span className="rounded-lg bg-muted px-3 py-2">
                  Czas serii: {item.durationSeconds} s
                </span>
              )}
            </div>

            <p className="whitespace-pre-wrap break-words text-sm">
              <strong>Częstotliwość:</strong> {item.frequency || 'Nie ustalono.'}
            </p>

            {item.notes && (
              <p className="whitespace-pre-wrap break-words text-sm text-muted-foreground">
                <strong>Uwagi:</strong> {item.notes}
              </p>
            )}
          </article>
        ))}
      </div>
    </div>
  );
}
