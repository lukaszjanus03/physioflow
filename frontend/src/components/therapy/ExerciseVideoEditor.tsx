'use client';

import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Loader2, Upload, Video, Youtube } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import { Confirm } from '@/components/therapy/TherapyUI';
import ExerciseVideoPlayer, { LocalVideoPreview } from '@/components/therapy/ExerciseVideoPlayer';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import {
  exerciseVideosApi,
  canonicalYoutubeUrl,
  videoError,
  videoFileError,
} from '@/lib/exercise-videos-api';
import { therapyApi, therapyOptions } from '@/lib/therapy-api';
import type { Exercise } from '@/lib/therapy-api';
import { useAuthStore } from '@/stores/auth-store';

export default function ExerciseVideoEditor({
  exerciseId,
  onClose,
}: {
  exerciseId: string;
  onClose: () => void;
}) {
  const owner = useAuthStore((state) => state.userId);
  const query = useQuery({
    queryKey: ['exercise-video-editor', owner, exerciseId],
    queryFn: ({ signal }) => therapyApi.exercise(exerciseId, signal),
    enabled: Boolean(owner),
    ...therapyOptions,
  });
  if (query.isSuccess && owner)
    return (
      <Editor key={`${owner}:${exerciseId}`} initial={query.data} owner={owner} onClose={onClose} />
    );
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-2xl overflow-y-auto rounded-2xl [&>*]:min-w-0">
        <DialogHeader>
          <DialogTitle>Wideo ćwiczenia</DialogTitle>
          <DialogDescription>Pobieramy aktualne dane i token wersji ćwiczenia.</DialogDescription>
        </DialogHeader>
        {query.isError ? (
          <div className="space-y-3">
            <p role="alert" className="text-sm text-destructive">
              {videoError(query.error)}
            </p>
            <Button type="button" variant="outline" onClick={() => void query.refetch()}>
              Spróbuj ponownie
            </Button>
          </div>
        ) : (
          <LoadingState title="Pobieranie ćwiczenia…" />
        )}
      </DialogContent>
    </Dialog>
  );
}

type Mode = 'None' | 'Upload' | 'YouTube';

function Editor({
  initial,
  owner,
  onClose,
}: {
  initial: Exercise;
  owner: string;
  onClose: () => void;
}) {
  const client = useQueryClient();
  const [base, setBase] = useState(initial);
  const [mode, setMode] = useState<Mode>(initial.video?.source ?? 'None');
  const [url, setUrl] = useState(initial.video?.youtubeUrl ?? '');
  const [file, setFile] = useState<File | null>(null);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [reading, setReading] = useState(false);
  const [progress, setProgress] = useState<number | null>(null);
  const [message, setMessage] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [blocked, setBlocked] = useState(false);
  const [remote, setRemote] = useState<Exercise | null>(null);
  const [compared, setCompared] = useState(false);
  const [detach, setDetach] = useState(false);
  const request = useRef<AbortController | null>(null);
  const dirty =
    mode !== (base.video?.source ?? 'None') ||
    file !== null ||
    url !== (base.video?.youtubeUrl ?? '');
  const guard = useUnsavedChanges(dirty, busy || reading);
  const canonical = mode === 'YouTube' ? canonicalYoutubeUrl(url) : null;
  const shownError =
    fieldError ||
    (mode === 'YouTube' && url.trim() && !canonical
      ? 'Podaj obsługiwany adres pojedynczego filmu YouTube.'
      : '');
  const noChange =
    mode === 'None'
      ? !base.video
      : mode === 'Upload'
        ? !file
        : !canonical || (base.video?.source === 'YouTube' && canonical === base.video.youtubeUrl);

  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
    },
    [],
  );

  function reset(current: Exercise) {
    setBase(current);
    setMode(current.video?.source ?? 'None');
    setUrl(current.video?.youtubeUrl ?? '');
    setFile(null);
    setRevision((old) => old + 1);
    setFieldError('');
  }

  async function readCurrent() {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setReading(true);
    try {
      const current = await therapyApi.exercise(base.id, controller.signal);
      if (request.current !== controller) return;
      setRemote(current);
      setCompared(false);
    } catch (error) {
      if (!controller.signal.aborted) setMessage(videoError(error));
    } finally {
      if (request.current === controller) {
        request.current = null;
        setReading(false);
      }
    }
  }

  function accept(keep: boolean) {
    if (!remote || (keep && !compared)) return;
    if (keep) setBase(remote);
    else reset(remote);
    setRemote(null);
    setBlocked(false);
    setMessage('');
    setFieldError('');
  }

  async function write() {
    if (request.current || busy || reading || blocked || noChange) return;
    if (mode === 'Upload' && (!file || videoFileError(file))) {
      setFieldError(file ? (videoFileError(file) ?? '') : 'Wybierz nagranie.');
      return;
    }
    if (mode === 'YouTube' && !canonical) {
      setFieldError('Podaj obsługiwany adres filmu YouTube, nie playlisty.');
      return;
    }
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setMessage('');
    setFieldError('');
    setProgress(null);
    setDetach(false);
    try {
      const saved =
        mode === 'Upload' && file
          ? await exerciseVideosApi.upload(base, file, controller.signal, (value) => {
              if (request.current === controller) setProgress(value);
            })
          : mode === 'YouTube' && canonical
            ? await exerciseVideosApi.youtube(base, canonical, controller.signal)
            : await exerciseVideosApi.detach(base, controller.signal);
      if (request.current !== controller) return;
      reset(saved);
      setBlocked(false);
      setRemote(null);
      void client.invalidateQueries({ queryKey: ['therapy-exercises', owner] });
      void client.invalidateQueries({ queryKey: ['therapy-exercise', owner, base.id] });
      toast.success(saved.video ? 'Materiał wideo zapisany.' : 'Materiał odłączony od ćwiczenia.');
    } catch (error) {
      if (request.current !== controller) return;
      setMessage(videoError(error));
      const status = isAxiosError(error) ? error.response?.status : undefined;
      if (!status || status === 409 || status === 404 || (status >= 500 && status !== 503)) {
        setBlocked(true);
        setRemote(null);
      }
    } finally {
      if (request.current === controller) {
        request.current = null;
        setBusy(false);
        setProgress(null);
      }
    }
  }

  return (
    <>
      <Dialog
        open
        onOpenChange={(open) => {
          if (!open) guard.confirmLeave(onClose);
        }}
      >
        <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-3xl overflow-y-auto rounded-2xl [&>*]:min-w-0">
          <DialogHeader className="pr-6 text-left">
            <DialogTitle>Wideo ćwiczenia</DialogTitle>
            <DialogDescription className="break-words leading-relaxed">
              {base.name}. Film zapisujesz osobno od instrukcji. Zmiany nie podmieniają materiałów w
              już zapisanych pozycjach i historii planów.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 rounded-xl border bg-muted/20 p-4">
            <p className="text-sm font-semibold">Aktualny materiał w bibliotece</p>
            {base.video ? (
              <ExerciseVideoPlayer video={base.video} title={base.name} />
            ) : (
              <p className="text-sm text-muted-foreground">To ćwiczenie nie ma filmu.</p>
            )}
          </div>

          {remote && (
            <section className="space-y-4 rounded-xl border border-amber-300 bg-amber-50/30 p-4">
              <h3 className="text-2xl">Aktualny stan serwera</h3>
              <p className="break-words text-sm">
                {remote.name} · {remote.isArchived ? 'Archiwalne' : 'Bieżące'}
              </p>
              {remote.video ? (
                <ExerciseVideoPlayer video={remote.video} title={remote.name} />
              ) : (
                <p className="text-sm">Na serwerze ćwiczenie nie ma filmu.</p>
              )}
              <p className="text-xs leading-relaxed text-muted-foreground">
                Lokalny wybór pliku i adres pozostają w formularzu. Wybranie sposobu rozstrzygnięcia
                nie zapisuje ani nie ponawia operacji automatycznie.
              </p>
              <label className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={compared}
                  onChange={(event) => setCompared(event.target.checked)}
                  className="mt-1"
                />
                Porównałem materiały. Świadomie zachowuję lokalny wybór do ewentualnego zastąpienia
                aktualnego materiału.
              </label>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => guard.confirmLeave(() => accept(false))}
                >
                  Przyjmij stan serwera
                </Button>
                <Button type="button" disabled={!compared} onClick={() => accept(true)}>
                  Zachowaj lokalny wybór
                </Button>
              </div>
            </section>
          )}

          <form
            className="space-y-5"
            noValidate
            onSubmit={(event) => {
              event.preventDefault();
              if (mode === 'None' && base.video) setDetach(true);
              else void write();
            }}
          >
            <fieldset disabled={busy || reading} className="space-y-5">
              <legend className="mb-3 text-sm font-semibold">Źródło materiału</legend>
              <div className="grid gap-2 sm:grid-cols-3">
                {(
                  [
                    ['None', 'Bez filmu', Video],
                    ['Upload', 'Własny plik', Upload],
                    ['YouTube', 'YouTube', Youtube],
                  ] as const
                ).map(([value, label, Icon]) => (
                  <label
                    key={value}
                    className={`flex min-h-12 cursor-pointer items-center gap-2 rounded-xl border p-3 text-sm ${mode === value ? 'border-primary bg-primary/5' : 'bg-card'}`}
                  >
                    <input
                      type="radio"
                      name="exercise-video-source"
                      value={value}
                      checked={mode === value}
                      onChange={() => {
                        setMode(value);
                        setFieldError('');
                      }}
                    />
                    <Icon size={17} aria-hidden="true" />
                    {label}
                  </label>
                ))}
              </div>
              {mode === 'None' && (
                <p className="text-sm leading-relaxed text-muted-foreground">
                  Odłączenie usuwa tylko powiązanie w bibliotece. Zapisane plany i ich historie
                  zachowują swój film. Wydane wcześniej linki do nagrania mogą działać do końca
                  pięciominutowej ważności.
                </p>
              )}
              {mode === 'Upload' && (
                <div className="space-y-3">
                  <label className="block space-y-2 text-sm font-medium">
                    <span>Wybierz nagranie</span>
                    <Input
                      key={revision}
                      type="file"
                      accept=".mp4,.webm,.mov,video/mp4,video/webm,video/quicktime"
                      className="h-auto min-h-11 py-2"
                      aria-invalid={Boolean(shownError)}
                      aria-describedby="exercise-video-help"
                      onChange={(event) => {
                        const next = event.target.files?.[0] ?? null;
                        setFile(next && !videoFileError(next) ? next : null);
                        setFieldError(next ? (videoFileError(next) ?? '') : '');
                        setRevision((old) => old + 1);
                      }}
                    />
                  </label>
                  <p
                    id="exercise-video-help"
                    className="text-xs leading-relaxed text-muted-foreground"
                  >
                    MP4, WebM lub MOV, maksymalnie 100 MiB. Odtwarzanie zależy również od kodeka.
                    Plik trafia przez API do prywatnego R2, nie do publicznego bucketa zdjęć.
                  </p>
                  {file && (
                    <>
                      <p className="break-words text-xs">
                        Wybrano: {file.name} · {(file.size / 1048576).toFixed(1)} MiB. To podgląd
                        lokalny, jeszcze niezapisany.
                      </p>
                      <LocalVideoPreview key={revision} file={file} />
                    </>
                  )}
                </div>
              )}
              {mode === 'YouTube' && (
                <div className="space-y-3">
                  <label className="block space-y-2 text-sm font-medium">
                    <span>Adres filmu YouTube</span>
                    <Input
                      type="url"
                      maxLength={2048}
                      placeholder="https://www.youtube.com/watch?v=…"
                      value={url}
                      aria-invalid={Boolean(shownError)}
                      aria-describedby="exercise-youtube-help"
                      onChange={(event) => {
                        setUrl(event.target.value);
                        setFieldError('');
                      }}
                    />
                  </label>
                  <p
                    id="exercise-youtube-help"
                    className="text-xs leading-relaxed text-muted-foreground"
                  >
                    Obsługiwane są linki do pojedynczego filmu, w tym watch, youtu.be, shorts, live
                    i embed. Zapis usunie parametry śledzenia i czas startu. Ten wariant nie wymaga
                    R2.
                  </p>
                  {canonical && (
                    <ExerciseVideoPlayer
                      video={{
                        id: `preview-${canonical}`,
                        source: 'YouTube',
                        youtubeUrl: canonical,
                        contentType: null,
                        sizeBytes: null,
                      }}
                      title="Podgląd niezapisanego adresu YouTube"
                    />
                  )}
                </div>
              )}
            </fieldset>
            {shownError && (
              <p role="alert" className="text-sm text-destructive">
                {shownError}
              </p>
            )}
            {message && (
              <p role="alert" className="text-sm leading-relaxed text-destructive">
                {message}
              </p>
            )}
            {busy && (
              <div role="status" className="space-y-2 rounded-xl border p-4">
                <p className="text-sm">
                  {mode === 'Upload'
                    ? progress === 100
                      ? 'Plik przesłany. Oczekiwanie na potwierdzenie zapisu…'
                      : `Przesyłanie nagrania${progress === null ? '…' : `: ${progress}%`}`
                    : 'Zapisywanie materiału…'}
                </p>
                {mode === 'Upload' && (
                  <>
                    <progress
                      max={100}
                      value={progress ?? undefined}
                      aria-label="Postęp wysyłania pliku"
                      className="h-2 w-full accent-primary"
                    />
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => request.current?.abort()}
                    >
                      Anuluj przesyłanie
                    </Button>
                  </>
                )}
              </div>
            )}
            {blocked && !remote && (
              <Button
                type="button"
                variant="outline"
                disabled={busy || reading}
                onClick={() => void readCurrent()}
              >
                {reading ? 'Pobieranie…' : 'Pobierz aktualny stan przed kolejną zmianą'}
              </Button>
            )}
            <div className="flex flex-wrap gap-3 border-t pt-4">
              <Button
                type="submit"
                disabled={busy || reading || blocked || noChange}
                className="gap-2"
              >
                {busy && (
                  <Loader2
                    size={16}
                    aria-hidden="true"
                    className="animate-spin motion-reduce:animate-none"
                  />
                )}
                {mode === 'None'
                  ? 'Odłącz materiał'
                  : base.video
                    ? 'Zastąp materiał'
                    : 'Zapisz materiał'}
              </Button>
              <Button
                type="button"
                variant="ghost"
                disabled={busy || reading}
                onClick={() => guard.confirmLeave(onClose)}
              >
                Zamknij
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
      {guard.discardDialog}
      {detach && (
        <Confirm
          title="Odłączyć materiał od biblioteki?"
          text="Istniejące pozycje planów i historyczne wersje zachowają własny materiał. Ta operacja nie usuwa nagrania z magazynu."
          busy={busy || reading}
          onClose={() => setDetach(false)}
          onConfirm={() => void write()}
        />
      )}
    </>
  );
}
