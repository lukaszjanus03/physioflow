'use client';

import { useEffect, useRef, useState } from 'react';
import { ExternalLink, Loader2, Play, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  exerciseVideosApi,
  canonicalYoutubeUrl,
  videoError,
  youtubeVideoId,
} from '@/lib/exercise-videos-api';
import type { ExerciseVideo } from '@/lib/therapy-api';
import { useAuthStore } from '@/stores/auth-store';

export default function ExerciseVideoPlayer({
  video,
  title,
}: {
  video?: ExerciseVideo | null;
  title: string;
}) {
  const owner = useAuthStore((state) => state.userId);
  if (!video || !owner) return null;
  return <Player key={`${owner}:${video.id}`} video={video} title={title} />;
}

function Player({ video, title }: { video: ExerciseVideo; title: string }) {
  const [started, setStarted] = useState(false);
  const [loading, setLoading] = useState(false);
  const [playback, setPlayback] = useState<{ url: string; expires: number } | null>(null);
  const [message, setMessage] = useState('');
  const request = useRef<AbortController | null>(null);
  const youtubeId = video.source === 'YouTube' ? youtubeVideoId(video.youtubeUrl ?? '') : null;
  const youtubeUrl =
    video.source === 'YouTube' ? canonicalYoutubeUrl(video.youtubeUrl ?? '') : null;

  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
    },
    [],
  );

  useEffect(() => {
    if (!playback) return;
    const timer = window.setTimeout(
      () => {
        setPlayback(null);
        setMessage('Dostęp do nagrania wygasł. Pobierz nowy dostęp, aby kontynuować.');
      },
      Math.max(0, playback.expires - Date.now()),
    );
    return () => window.clearTimeout(timer);
  }, [playback]);

  async function open() {
    if (request.current) return;
    setMessage('');
    if (video.source === 'YouTube') {
      setStarted(true);
      return;
    }
    const controller = new AbortController();
    request.current = controller;
    setLoading(true);
    setPlayback(null);
    try {
      const result = await exerciseVideosApi.playback(video.id, controller.signal);
      const url = new URL(result.url);
      const expires = Date.parse(result.expiresAt);
      if (
        url.protocol !== 'https:' ||
        url.username ||
        url.password ||
        !Number.isFinite(expires) ||
        expires <= Date.now()
      )
        throw new Error('invalid-playback');
      if (request.current === controller) setPlayback({ url: result.url, expires });
    } catch (error) {
      if (!controller.signal.aborted) setMessage(videoError(error));
    } finally {
      if (request.current === controller) {
        request.current = null;
        setLoading(false);
      }
    }
  }

  const invalidYoutube = video.source === 'YouTube' && !youtubeId;
  return (
    <section
      className="mt-4 min-w-0 space-y-3 rounded-xl border bg-muted/20 p-4"
      aria-label={`Wideo: ${title}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold">
          {video.source === 'YouTube' ? 'Film YouTube' : 'Własne nagranie'}
        </p>
        {video.source === 'Upload' && video.sizeBytes != null && (
          <span className="text-xs text-muted-foreground">
            {(video.sizeBytes / 1048576).toFixed(1)} MiB
          </span>
        )}
      </div>
      {invalidYoutube ? (
        <p role="alert" className="text-sm text-destructive">
          Nieobsługiwany adres materiału. Instrukcja tekstowa pozostaje dostępna.
        </p>
      ) : video.source === 'YouTube' && started ? (
        <>
          <iframe
            className="aspect-video w-full rounded-lg border-0"
            src={`https://www.youtube-nocookie.com/embed/${youtubeId}`}
            title={`Wideo: ${title}`}
            allow="encrypted-media; picture-in-picture; fullscreen"
            allowFullScreen
            referrerPolicy="strict-origin-when-cross-origin"
          />
          {youtubeUrl && (
            <a
              href={youtubeUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 text-xs text-primary underline underline-offset-4"
            >
              <ExternalLink size={14} aria-hidden="true" />
              Otwórz na YouTube
            </a>
          )}
          <p className="text-xs leading-relaxed text-muted-foreground">
            Dostępność filmu i możliwość osadzania zależą od YouTube. Jeśli odtwarzacz nie działa,
            użyj linku powyżej.
          </p>
        </>
      ) : playback ? (
        <video
          key={playback.url}
          src={playback.url}
          controls
          playsInline
          preload="metadata"
          crossOrigin="anonymous"
          aria-label={`Nagranie: ${title}`}
          className="aspect-video w-full rounded-lg bg-black object-contain"
          onError={() =>
            setMessage(
              'Nie można odtworzyć nagrania. Sprawdź połączenie, CORS magazynu i obsługę kodeka; możesz pobrać nowy dostęp.',
            )
          }
        />
      ) : (
        <p className="text-xs leading-relaxed text-muted-foreground">
          {video.source === 'YouTube'
            ? 'Odtwarzacz YouTube załaduje się dopiero po kliknięciu. Film nie uruchomi się automatycznie.'
            : 'Dostęp do prywatnego nagrania pobierzemy dopiero po kliknięciu. Link jest ważny przez 5 minut.'}
        </p>
      )}
      {!invalidYoutube && !(video.source === 'YouTube' && started) && (!playback || message) && (
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={loading}
          onClick={() => void open()}
          className="gap-2"
        >
          {loading ? (
            <Loader2
              size={15}
              aria-hidden="true"
              className="animate-spin motion-reduce:animate-none"
            />
          ) : message ? (
            <RefreshCw size={15} aria-hidden="true" />
          ) : (
            <Play size={15} aria-hidden="true" />
          )}
          {loading
            ? 'Pobieranie dostępu…'
            : message
              ? 'Pobierz nowy dostęp'
              : video.source === 'YouTube'
                ? 'Załaduj odtwarzacz'
                : 'Pokaż nagranie'}
        </Button>
      )}
      {message && (
        <p role="alert" className="text-xs leading-relaxed text-destructive">
          {message}
        </p>
      )}
    </section>
  );
}

export function LocalVideoPreview({ file }: { file: File }) {
  const player = useRef<HTMLVideoElement>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const element = player.current;
    if (!element) return;
    const url = URL.createObjectURL(file);
    element.src = url;
    return () => {
      element.pause();
      element.removeAttribute('src');
      element.load();
      URL.revokeObjectURL(url);
    };
  }, [file]);
  return (
    <div className="space-y-2">
      <video
        ref={player}
        controls
        playsInline
        preload="metadata"
        aria-label="Lokalny podgląd wybranego nagrania"
        className="aspect-video w-full rounded-xl bg-black object-contain"
        onError={() => setFailed(true)}
      />
      {failed && (
        <p role="alert" className="text-xs text-destructive">
          Przeglądarka nie odtwarza tego pliku lub kodeka. MP4, WebM i MOV są kontenerami; samo
          rozszerzenie nie gwarantuje odtwarzania. Wybierz zgodne nagranie, np. MP4 z H.264/AAC.
        </p>
      )}
    </div>
  );
}
