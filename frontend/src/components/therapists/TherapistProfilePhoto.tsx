'use client';

import * as Dialog from '@radix-ui/react-dialog';
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import {
  Camera,
  Loader2,
  Trash2,
  Upload,
} from 'lucide-react';
import {
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';
import type { ChangeEvent } from 'react';

import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import {
  photoError,
  profileKeys,
  therapistProfileApi,
} from '@/lib/therapist-profile-api';
import type {
  MarketplaceProfile,
} from '@/lib/therapist-profile-api';

function BlobImage({ blob }: { blob: Blob }) {
  const imageRef = useRef<HTMLImageElement>(null);

  useEffect(() => {
    const image = imageRef.current;
    if (!image) return;

    const url = URL.createObjectURL(blob);
    image.src = url;

    return () => {
      image.removeAttribute('src');
      URL.revokeObjectURL(url);
    };
  }, [blob]);

  // eslint-disable-next-line @next/next/no-img-element -- Private photos are authenticated blobs, not optimizer URLs.
  return <img ref={imageRef} alt="Zdjęcie profilowe terapeuty" className="h-40 w-40 rounded-2xl border object-cover" />;
}

export default function TherapistProfilePhoto({
  profile,
  accountId,
  blocked,
  onSaved,
  onBusyChange,
  onSelectionChange,
}: {
  profile: MarketplaceProfile;
  accountId: string;
  blocked: boolean;
  onSaved: (profile: MarketplaceProfile) => void;
  onBusyChange: (busy: boolean) => void;
  onSelectionChange: (selected: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const sending = useRef(false);

  const [file, setFile] = useState<File | null>(null);
  const [validation, setValidation] = useState('');
  const [success, setSuccess] = useState('');
  const [confirmDelete, setConfirmDelete] =
    useState(false);

  const query = useQuery({
    queryKey: profileKeys.photo(accountId, profile.id),
    queryFn: ({ signal }) =>
      therapistProfileApi.photo(profile.id, signal),
    enabled: Boolean(profile.photoUrl),
    staleTime: 0,
    refetchOnWindowFocus: false,
    retry: false,
  });

  const mutation = useMutation({
    mutationFn: (selected: File | null) =>
      selected
        ? therapistProfileApi.uploadPhoto(selected)
        : therapistProfileApi.deletePhoto(),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: async (data, selected) => {
      onSaved(data);

      await queryClient.invalidateQueries({
        queryKey: profileKeys.photo(
          accountId,
          profile.id,
        ),
      });

      setFile(null);

      if (input.current) {
        input.current.value = '';
      }

      onSelectionChange(false);
      setConfirmDelete(false);
      setSuccess(
        selected
          ? 'Zdjęcie zostało zapisane.'
          : 'Zdjęcie zostało usunięte.',
      );
    },
    onSettled: () => {
      sending.current = false;
      onBusyChange(false);
    },
  });

  const disabled = blocked || mutation.isPending;

  function choose(
    event: ChangeEvent<HTMLInputElement>,
  ) {
    const selected = event.target.files?.[0];
    if (!selected) return;

    setSuccess('');
    setValidation('');
    mutation.reset();

    const validType = selected.type
      ? [
          'image/jpeg',
          'image/png',
          'image/webp',
        ].includes(selected.type)
      : /\.(jpe?g|png|webp)$/i.test(selected.name);

    if (
      !validType ||
      selected.size === 0 ||
      selected.size > 5 * 1024 * 1024
    ) {
      setValidation(
        !validType
          ? 'Wybierz zdjęcie JPEG, PNG lub WebP.'
          : 'Zdjęcie musi mieć od 1 bajta do 5 MiB.',
      );

      event.target.value = '';
      return;
    }

    setFile(selected);
    onSelectionChange(true);
  }

  function clearSelection() {
    setFile(null);
    setValidation('');
    mutation.reset();

    if (input.current) {
      input.current.value = '';
    }

    onSelectionChange(false);
  }

  function send(selected: File | null) {
    if (disabled || sending.current) return;

    sending.current = true;
    setSuccess('');
    setValidation('');
    mutation.mutate(selected);
  }

  return (
    <section
      aria-labelledby={`${id}-heading`}
      className="rounded-2xl border bg-card p-5 sm:p-6"
    >
      <h2
        id={`${id}-heading`}
        className="text-3xl"
      >
        Zdjęcie profilowe
      </h2>

      <p className="mt-2 text-sm text-muted-foreground">
        Zdjęcie zapisujesz osobno od danych formularza.
      </p>

      <div className="mt-5 flex flex-col gap-6 sm:flex-row sm:items-start">
        <div className="flex shrink-0 justify-center">
          {file ? (
            <BlobImage blob={file} />
          ) : profile.photoUrl && query.data ? (
            <BlobImage blob={query.data} />
          ) : (
            <div className="flex h-40 w-40 items-center justify-center rounded-2xl border bg-primary/5">
              <Camera
                aria-hidden="true"
                className="h-12 w-12 text-primary/60"
              />
              <span className="sr-only">
                Brak podglądu zdjęcia
              </span>
            </div>
          )}
        </div>

        <div className="min-w-0 flex-1 space-y-4">
          <p
            id={`${id}-help`}
            className="text-sm text-muted-foreground"
          >
            JPEG, PNG lub WebP, maksymalnie 5 MiB.
            Zdjęcie będzie widoczne dla innych po
            opublikowaniu profilu.
          </p>

          <label
            htmlFor={`${id}-file`}
            className="block text-sm font-medium"
          >
            Wybierz plik zdjęcia
          </label>

          <input
            ref={input}
            id={`${id}-file`}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            disabled={disabled}
            onChange={choose}
            aria-describedby={`${id}-help`}
            className="block w-full text-sm file:mr-3 file:rounded-md file:border file:bg-background file:px-3 file:py-2 disabled:opacity-50"
          />

          {blocked && (
            <p className="text-xs text-muted-foreground">
              Przed zmianą zdjęcia zapisz lub odrzuć
              zmiany formularza.
            </p>
          )}

          {file && (
            <div className="space-y-3">
              <p className="break-words text-xs text-muted-foreground">
                Wybrano: {file.name}. To podgląd —
                zdjęcie nie zostało jeszcze zapisane.
              </p>

              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  disabled={disabled}
                  onClick={() => send(file)}
                >
                  {mutation.isPending ? (
                    <Loader2
                      aria-hidden="true"
                      className="mr-2 h-4 w-4 animate-spin"
                    />
                  ) : (
                    <Upload
                      aria-hidden="true"
                      className="mr-2 h-4 w-4"
                    />
                  )}

                  {mutation.isPending
                    ? 'Zapisywanie zdjęcia…'
                    : 'Zapisz zdjęcie'}
                </Button>

                <Button
                  type="button"
                  variant="outline"
                  disabled={disabled}
                  onClick={clearSelection}
                >
                  Anuluj wybór
                </Button>
              </div>
            </div>
          )}

          {profile.photoUrl && !file && (
            <Button
              type="button"
              variant="outline"
              disabled={disabled}
              onClick={() => {
                mutation.reset();
                setSuccess('');
                setConfirmDelete(true);
              }}
            >
              <Trash2
                aria-hidden="true"
                className="mr-2 h-4 w-4"
              />
              Usuń zdjęcie
            </Button>
          )}

          {profile.photoUrl &&
            query.isPending &&
            !file && (
              <LoadingState
                title="Wczytywanie zdjęcia…"
                className="min-h-24 border-0 p-3"
              />
            )}

          {profile.photoUrl &&
            query.isError &&
            !file && (
              <div className="space-y-2">
                <p
                  role="alert"
                  className="text-sm text-destructive"
                >
                  Nie udało się pobrać zdjęcia.{' '}
                  {photoError(query.error)}
                </p>

                <Button
                  type="button"
                  variant="outline"
                  disabled={
                    disabled || query.isFetching
                  }
                  onClick={() => void query.refetch()}
                >
                  Odśwież podgląd
                </Button>
              </div>
            )}

          {validation && (
            <p
              role="alert"
              className="text-sm text-destructive"
            >
              {validation}
            </p>
          )}

          {mutation.isError && (
            <p
              role="alert"
              className="text-sm text-destructive"
            >
              {photoError(mutation.error)}
            </p>
          )}

          {success && (
            <p
              role="status"
              className="text-sm text-primary"
            >
              {success}
            </p>
          )}
        </div>
      </div>

      <Dialog.Root
        open={confirmDelete}
        onOpenChange={(open) => {
          if (!mutation.isPending) {
            setConfirmDelete(open);
          }
        }}
      >
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

          <Dialog.Content
            className="fixed left-1/2 top-1/2 z-50 w-[calc(100%_-_2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl border bg-background p-6 shadow-xl"
            onEscapeKeyDown={(event) => {
              if (mutation.isPending) {
                event.preventDefault();
              }
            }}
            onPointerDownOutside={(event) => {
              if (mutation.isPending) {
                event.preventDefault();
              }
            }}
          >
            <Dialog.Title className="text-3xl">
              Usunąć zdjęcie?
            </Dialog.Title>

            <Dialog.Description className="mt-3 text-sm text-muted-foreground">
              Zdjęcie zniknie z profilu. Pozostałe dane
              nie zostaną usunięte.
            </Dialog.Description>

            {mutation.isError && (
              <p
                role="alert"
                className="mt-4 text-sm text-destructive"
              >
                {photoError(mutation.error)}
              </p>
            )}

            <div className="mt-6 flex flex-wrap justify-end gap-3">
              <Button
                type="button"
                variant="outline"
                disabled={mutation.isPending}
                onClick={() => setConfirmDelete(false)}
              >
                Anuluj
              </Button>

              <Button
                type="button"
                variant="destructive"
                disabled={disabled}
                onClick={() => send(null)}
              >
                {mutation.isPending
                  ? 'Usuwanie…'
                  : 'Usuń zdjęcie'}
              </Button>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </section>
  );
}