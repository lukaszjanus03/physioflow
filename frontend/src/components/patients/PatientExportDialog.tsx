'use client';

import { useEffect, useRef, useState } from 'react';
import { FileDown, Loader2, ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  allApprovedExportRecords,
  exportAccessLost,
  exportDateError,
  patientExportApi,
  patientExportError,
  type ExportDates,
  type ExportProgress,
  type PdfDocumentation,
} from '@/lib/patient-export-api';
import { useAuthStore } from '@/stores/auth-store';
import type { PatientDetail } from '@/types/api';

type Prepared = { patient: PatientDetail; documentation?: PdfDocumentation; at: Date };

export default function PatientExportDialog({ patientId }: { patientId: string }) {
  const owner = useAuthStore((s) => s.userId);
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <Button variant="outline" type="button" onClick={() => setOpen(true)}>
        <FileDown aria-hidden="true" className="mr-2 h-4 w-4" />
        Eksport PDF
      </Button>
      {open && (
        <ExportForm
          key={`${owner}:${patientId}`}
          patientId={patientId}
          owner={owner}
          close={() => setOpen(false)}
        />
      )}
    </Dialog>
  );
}

function ExportForm({
  patientId,
  owner,
  close,
}: {
  patientId: string;
  owner: string | null;
  close: () => void;
}) {
  const [include, setInclude] = useState(false);
  const [dates, setDates] = useState<ExportDates>({ from: '', to: '' });
  const [prepared, setPrepared] = useState<Prepared | null>(null);
  const [busy, setBusy] = useState<'loading' | 'generating' | null>(null);
  const [progress, setProgress] = useState<ExportProgress | null>(null);
  const [error, setError] = useState('');
  const [denied, setDenied] = useState(false);
  const request = useRef<AbortController | null>(null);
  const validation = include ? exportDateError(dates) : null;
  useEffect(() => () => request.current?.abort(), []);

  function current(controller: AbortController) {
    const auth = useAuthStore.getState();
    return (
      !controller.signal.aborted &&
      auth.isAuthenticated &&
      auth.userId === owner &&
      auth.roles.includes('Therapist')
    );
  }
  function reset() {
    setPrepared(null);
    setError('');
    setProgress(null);
  }
  async function prepare() {
    if (request.current || validation || denied) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy('loading');
    reset();
    try {
      const entries = include
        ? await allApprovedExportRecords(patientId, dates, controller.signal, setProgress)
        : undefined;
      const patient = await patientExportApi.patient(patientId, controller.signal);
      if (!current(controller)) return;
      if (patient.id !== patientId) throw new Error('wrong-patient');
      setPrepared({ patient, documentation: entries ? { ...dates, entries } : undefined, at: new Date() });
    } catch (failure) {
      if (!controller.signal.aborted) {
        setError(patientExportError(failure));
        setDenied(exportAccessLost(failure));
      }
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setBusy(null);
    }
  }
  async function download() {
    if (!prepared || request.current || denied) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy('generating');
    setError('');
    try {
      // Recheck card access before the expensive local rendering step.
      const patient = await patientExportApi.patient(patientId, controller.signal);
      if (!current(controller)) return;
      if (patient.id !== patientId) throw new Error('wrong-patient');
      const { downloadPatientCardPdf } = await import('@/components/patients/PatientCardPdf');
      if (!current(controller)) return;
      await downloadPatientCardPdf(patient, {
        documentation: prepared.documentation,
        signal: controller.signal,
        canDownload: () => current(controller),
      });
      if (current(controller)) close();
    } catch (failure) {
      if (!controller.signal.aborted) {
        setPrepared(null);
        setError(patientExportError(failure));
        setDenied(exportAccessLost(failure));
      }
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setBusy(null);
    }
  }

  return (
    <DialogContent
      className="max-h-[90dvh] w-[calc(100%-1.5rem)] max-w-xl overflow-y-auto rounded-2xl"
      onEscapeKeyDown={() => request.current?.abort()}
    >
      <DialogHeader>
        <DialogTitle className="text-3xl">Eksport karty do PDF</DialogTitle>
        <DialogDescription>
          Dotychczasowa karta pacjenta z opcjonalną zatwierdzoną dokumentacją wizyt. Plik powstaje lokalnie w
          przeglądarce.
        </DialogDescription>
      </DialogHeader>
      <div className="flex gap-3 rounded-xl border border-primary/20 bg-primary/5 p-4 text-sm leading-6">
        <ShieldCheck className="mt-1 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
        <p>
          PDF zawiera dane osobowe i medyczne, a po dołączeniu wpisów także wewnętrzną dokumentację terapeuty.
          Zadbaj o bezpieczne przechowywanie pliku. Eksport nie publikuje go pacjentowi.
        </p>
      </div>
      <fieldset disabled={Boolean(busy) || denied} className="space-y-4 disabled:opacity-60">
        <legend className="mb-3 font-semibold">Zakres eksportu</legend>
        <p className="text-sm text-muted-foreground">
          Zawsze: dane osobowe i kontaktowe, historia medyczna, leki i notatki ogólne.
        </p>
        <label className="flex cursor-pointer items-start gap-3 rounded-xl border p-4 text-sm">
          <input
            type="checkbox"
            className="mt-1 accent-[hsl(var(--primary))]"
            checked={include}
            onChange={(e) => {
              setInclude(e.target.checked);
              reset();
            }}
          />
          <span>
            <span className="block font-semibold">Dołącz dokumentację wizyt</span>
            <span className="mt-1 block text-xs leading-6 text-muted-foreground">
              Wyłącznie ostatnie zatwierdzone wersje, bez szkiców, czatu, planów i map bólu.
            </span>
          </span>
        </label>
        {include && (
          <div className="space-y-3 rounded-xl border p-4">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <label className="space-y-2 text-sm">
                <span>Data wizyty od</span>
                <input
                  type="date"
                  min="1900-01-01"
                  max="9999-12-31"
                  value={dates.from}
                  onChange={(e) => {
                    setDates((old) => ({ ...old, from: e.target.value }));
                    reset();
                  }}
                  className="h-10 w-full rounded-md border bg-background px-3"
                />
              </label>
              <label className="space-y-2 text-sm">
                <span>Data wizyty do</span>
                <input
                  type="date"
                  min="1900-01-01"
                  max="9999-12-31"
                  value={dates.to}
                  onChange={(e) => {
                    setDates((old) => ({ ...old, to: e.target.value }));
                    reset();
                  }}
                  className="h-10 w-full rounded-md border bg-background px-3"
                />
              </label>
            </div>
            <p className="text-xs leading-6 text-muted-foreground">
              Puste obie daty: cała historia. Granice są włączne i dotyczą dat wizyt w Europe/Warsaw, nie dat
              zatwierdzenia. Wpisy bez wizyty są dostępne tylko bez obu filtrów.
            </p>
          </div>
        )}
      </fieldset>
      {validation && (
        <p role="alert" className="text-sm text-destructive">
          {validation}
        </p>
      )}
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      {busy && (
        <p role="status" className="flex items-center gap-2 text-sm text-primary">
          <Loader2 aria-hidden="true" className="h-4 w-4 animate-spin motion-reduce:animate-none" />
          {busy === 'generating'
            ? 'Generowanie PDF…'
            : progress
              ? `Pobieranie dokumentacji: ${progress.entries}/${progress.total}, strona ${progress.page}…`
              : 'Sprawdzanie danych eksportu…'}
        </p>
      )}
      {prepared && !busy && (
        <section
          aria-label="Podsumowanie eksportu"
          className="space-y-2 rounded-xl border bg-muted/30 p-4 text-sm leading-6"
        >
          <p className="font-semibold">
            {prepared.patient.firstName} {prepared.patient.lastName}
          </p>
          <p>
            {prepared.documentation
              ? `Karta i zatwierdzona dokumentacja: ${prepared.documentation.entries.length} wpisów.`
              : 'Sama karta pacjenta, bez dokumentacji wizyt.'}
          </p>
          {prepared.documentation && !prepared.documentation.entries.length && (
            <p role="status">
              Brak zatwierdzonych wpisów w wybranym zakresie. PDF będzie zawierał kartę i informację o braku
              wpisów.
            </p>
          )}
          <p className="text-xs text-muted-foreground">
            Dane pobrano: {prepared.at.toLocaleTimeString('pl-PL')}. Jeśli w trakcie pobierania zmieniano
            wizyty lub dokumentację, przygotuj eksport ponownie. API nie zapewnia wspólnej migawki wszystkich
            stron.
          </p>
        </section>
      )}
      <DialogFooter className="gap-2">
        <Button type="button" variant="outline" onClick={close}>
          {busy ? 'Anuluj eksport' : 'Zamknij'}
        </Button>
        {prepared && !busy && (
          <Button type="button" variant="outline" onClick={() => void prepare()}>
            Przygotuj ponownie
          </Button>
        )}
        <Button
          type="button"
          disabled={Boolean(busy) || Boolean(validation) || denied}
          onClick={() => void (prepared ? download() : prepare())}
        >
          {prepared ? 'Pobierz PDF' : 'Przygotuj eksport'}
        </Button>
      </DialogFooter>
    </DialogContent>
  );
}
