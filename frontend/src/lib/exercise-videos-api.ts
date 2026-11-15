import { isAxiosError, isCancel } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { Exercise } from '@/lib/therapy-api';

export const MAX_VIDEO_BYTES = 100 * 1024 * 1024;
const MIME_TYPES: Record<string, string> = {
  mp4: 'video/mp4',
  webm: 'video/webm',
  mov: 'video/quicktime',
};

export function videoFileError(file: File): string | null {
  if (file.size < 1) return 'Plik jest pusty.';
  if (file.size > MAX_VIDEO_BYTES) return 'Maksymalny rozmiar nagrania to 100 MiB.';
  const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
  const mime = MIME_TYPES[extension];
  if (!mime) return 'Wybierz plik MP4, WebM albo MOV.';
  if (file.type && file.type.toLowerCase() !== mime)
    return 'Typ pliku nie odpowiada jego rozszerzeniu.';
  return null;
}

export function youtubeVideoId(value: string): string | null {
  if (!value || value.length > 2048) return null;
  try {
    const url = new URL(value.trim());
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.port)
      return null;
    if (url.searchParams.getAll('v').length > 1) return null;
    const host = url.hostname.toLowerCase();
    const parts = url.pathname.split('/').filter(Boolean);
    let id: string | null = null;
    if (host === 'youtu.be' && parts.length === 1) id = parts[0];
    if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com'].includes(host)) {
      if (url.pathname === '/watch') id = url.searchParams.get('v');
      else if (parts.length === 2 && ['shorts', 'embed', 'live'].includes(parts[0])) id = parts[1];
    }
    if (
      ['youtube-nocookie.com', 'www.youtube-nocookie.com'].includes(host) &&
      parts.length === 2 &&
      parts[0] === 'embed'
    )
      id = parts[1];
    return id && /^[A-Za-z0-9_-]{11}$/.test(id) ? id : null;
  } catch {
    return null;
  }
}

export function canonicalYoutubeUrl(value: string): string | null {
  const id = youtubeVideoId(value);
  return id ? `https://www.youtube.com/watch?v=${id}` : null;
}

export function videoError(error: unknown): string {
  if (isCancel(error)) return 'Przesyłanie anulowano. Sprawdź stan serwera przed kolejną zmianą.';
  const status = isAxiosError(error) ? error.response?.status : undefined;
  const code = isAxiosError(error)
    ? (error.response?.data as { code?: string } | undefined)?.code
    : undefined;
  if (code === 'storage_unavailable' || status === 503)
    return 'Magazyn nagrań jest niedostępny. Sprawdź konfigurację prywatnego bucketa R2 lub spróbuj później. YouTube nie wymaga R2.';
  if (code === 'video_not_uploaded')
    return 'Ten materiał nie jest własnym nagraniem. Pobierz aktualne dane ćwiczenia.';
  if (status === 400)
    return 'Sprawdź adres YouTube, format i rozmiar pliku oraz aktualność danych.';
  if (status === 401) return 'Sesja wygasła. Zaloguj się ponownie.';
  if (status === 403) return 'Nie masz uprawnień do tego materiału.';
  if (status === 404) return 'Nie znaleziono materiału dostępnego dla Twojego konta.';
  if (status === 409)
    return 'Ćwiczenie zmieniło się w innym miejscu. Porównaj aktualne dane przed kolejnym zapisem.';
  if (status === 413)
    return 'Serwer lub proxy odrzucił rozmiar żądania. Limit nagrania wynosi 100 MiB.';
  return 'Operacja nie została potwierdzona. Sprawdź połączenie i aktualny stan przed ponownym zapisem.';
}

export const exerciseVideosApi = {
  upload: (
    exercise: Exercise,
    file: File,
    signal: AbortSignal,
    progress: (percent: number | null) => void,
  ) => {
    const data = new FormData();
    const extension = file.name.split('.').pop()?.toLowerCase() ?? '';
    const upload = file.type
      ? file
      : new File([file], file.name, {
          type: MIME_TYPES[extension],
          lastModified: file.lastModified,
        });
    data.append('file', upload);
    data.append('version', exercise.version);
    return apiClient
      .post<Exercise>(`/api/exercises/${exercise.id}/video/upload`, data, {
        headers: { 'Content-Type': undefined },
        signal,
        timeout: 600000,
        onUploadProgress: (event) =>
          progress(
            event.total ? Math.min(100, Math.round((event.loaded / event.total) * 100)) : null,
          ),
      })
      .then((response) => response.data);
  },
  youtube: (exercise: Exercise, url: string, signal: AbortSignal) =>
    apiClient
      .put<Exercise>(
        `/api/exercises/${exercise.id}/video/youtube`,
        { version: exercise.version, url },
        { signal, timeout: 15000 },
      )
      .then((response) => response.data),
  detach: (exercise: Exercise, signal: AbortSignal) =>
    apiClient
      .delete<Exercise>(`/api/exercises/${exercise.id}/video`, {
        data: { version: exercise.version },
        signal,
        timeout: 15000,
      })
      .then((response) => response.data),
  playback: (videoId: string, signal: AbortSignal) =>
    apiClient
      .get<{
        url: string;
        expiresAt: string;
      }>(`/api/exercise-videos/${videoId}/playback`, { signal, timeout: 15000 })
      .then((response) => response.data),
};
