'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import {
  Loader2,
  RotateCcw,
  Save,
} from 'lucide-react';
import { useRef, useState } from 'react';
import { useForm } from 'react-hook-form';

import TherapistProfilePhoto from './TherapistProfilePhoto';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import {
  Input,
  Label,
  Textarea,
} from '@/components/ui/primitives';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { getApiMessage } from '@/lib/portal-api';
import {
  profileFields,
  profileKeys,
  profileServerErrors,
  profileToValues,
  therapistProfileApi,
  validateProfile,
} from '@/lib/therapist-profile-api';
import type {
  MarketplaceProfile,
  ProfileValues,
} from '@/lib/therapist-profile-api';

const sections = [
  {
    id: 'basic',
    title: 'Dane podstawowe',
    description:
      'Imię i nazwisko oraz podstawowe informacje o gabinecie.',
  },
  {
    id: 'professional',
    title: 'Profil zawodowy',
    description:
      'Opisz obszar swojej pracy, doświadczenie i wykształcenie.',
  },
  {
    id: 'address',
    title: 'Adres gabinetu',
    description:
      'Miejscowość uzupełniasz w danych podstawowych.',
  },
  {
    id: 'contact',
    title: 'Publiczny kontakt',
    description:
      'Te dane będą widoczne w opublikowanym profilu. Nie muszą być takie same jak dane konta.',
  },
  {
    id: 'visit',
    title: 'Informacje przed wizytą',
    description:
      'Pomóż pacjentowi dotrzeć do gabinetu i przygotować się do spotkania.',
  },
] as const;

export default function TherapistProfileForm({
  profile,
  accountId,
}: {
  profile: MarketplaceProfile;
  accountId: string;
}) {
  const queryClient = useQueryClient();
  const submitting = useRef(false);

  const [saved, setSaved] = useState(false);
  const [photoBusy, setPhotoBusy] = useState(false);
  const [photoSelected, setPhotoSelected] =
    useState(false);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    setFocus,
    formState: { errors, isDirty },
  } = useForm<ProfileValues>({
    defaultValues: profileToValues(profile),
  });

  const specializations = useQuery({
    queryKey:
      profileKeys.specializations(accountId),
    queryFn: ({ signal }) =>
      therapistProfileApi.specializations(signal),
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });

  function acceptProfile(data: MarketplaceProfile) {
    reset(profileToValues(data));

    queryClient.setQueryData(
      profileKeys.own(accountId),
      data,
    );

    void queryClient.invalidateQueries({
      queryKey: ['patient-catalog'],
    });

    void queryClient.invalidateQueries({
      queryKey: ['patient-therapist'],
    });
  }

  const mutation = useMutation({
    mutationFn: therapistProfileApi.save,
    retry: false,
    onSuccess: (data) => {
      acceptProfile(data);
      setSaved(true);
    },
    onError: (error) => {
      for (const [name, message] of Object.entries(
        profileServerErrors(error),
      )) {
        setError(name as keyof ProfileValues, {
          type: 'server',
          message,
        });
      }
    },
    onSettled: () => {
      submitting.current = false;
    },
  });

  const busy = mutation.isPending || photoBusy;
  const formBlocked = busy || photoSelected;

  const { confirmLeave, discardDialog } =
    useUnsavedChanges(
      isDirty || photoSelected,
      busy,
    );

  function submit(values: ProfileValues) {
    if (formBlocked || submitting.current) return;

    clearErrors();
    setSaved(false);

    const problems = validateProfile(values);
    const dictionary = specializations.data;

    if (
      dictionary &&
      values.specializations.some(
        (code) =>
          !dictionary.some(
            (item) => item.code === code,
          ),
      )
    ) {
      problems.specializations =
        'Wybierz specjalizacje z aktualnego słownika.';
    }

    const entries = Object.entries(problems);

    for (const [name, message] of entries) {
      setError(name as keyof ProfileValues, {
        type: 'validate',
        message,
      });
    }

    if (entries.length) {
      setFocus(
        entries[0][0] as keyof ProfileValues,
      );
      return;
    }

    submitting.current = true;
    mutation.mutate(values);
  }

  const missing =
    profile.missingFieldsForPublication.map(
      (name) =>
        profileFields.find(
          (field) => field.name === name,
        )?.label ?? name,
    );

  return (
    <>
      <TherapistProfilePhoto
        profile={profile}
        accountId={accountId}
        blocked={mutation.isPending || isDirty}
        onSaved={(data) => {
          acceptProfile(data);
          setSaved(false);
        }}
        onBusyChange={setPhotoBusy}
        onSelectionChange={setPhotoSelected}
      />

      <form
        noValidate
        onSubmit={(event) =>
          void handleSubmit(submit)(event)
        }
        aria-busy={busy}
        className="space-y-6"
      >
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-card p-5">
          <div>
            <h2 className="text-3xl">
              Dane profilu
            </h2>

            <p className="mt-1 text-xs text-muted-foreground">
              Imię i nazwisko są wymagane. Pozostałe
              wymagania publikacji podajemy poniżej.
            </p>
          </div>

          <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
            {profile.isPublished
              ? 'Opublikowany'
              : 'Nieopublikowany'}
          </span>
        </div>

        {photoSelected && (
          <p
            role="status"
            className="rounded-xl border bg-primary/5 p-4 text-sm"
          >
            Najpierw zapisz zdjęcie lub anuluj jego
            wybór. Potem możesz edytować dane profilu.
          </p>
        )}

        <fieldset
          disabled={formBlocked}
          className="min-w-0 space-y-6"
        >
          {sections.map((section, index) => (
            <section
              key={section.id}
              aria-labelledby={`profile-${section.id}-heading`}
              className="space-y-5 rounded-2xl border bg-card p-5 sm:p-6"
            >
              <header className="flex items-start gap-3">
                <span
                  aria-hidden="true"
                  className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-xs font-semibold text-primary"
                >
                  0{index + 1}
                </span>

                <div>
                  <h2
                    id={`profile-${section.id}-heading`}
                    className="text-3xl"
                  >
                    {section.title}
                  </h2>

                  <p className="mt-2 text-sm text-muted-foreground">
                    {section.description}
                  </p>
                </div>
              </header>

              {section.id === 'professional' && (
                <div className="space-y-3 rounded-xl border bg-muted/20 p-4">
                  <p
                    id="profile-specializations-label"
                    className="text-sm font-medium"
                  >
                    Specjalizacje — wybierz
                    maksymalnie 8
                  </p>

                  {specializations.isPending ? (
                    <LoadingState
                      title="Wczytywanie specjalizacji…"
                      className="min-h-24 border-0 p-3"
                    />
                  ) : specializations.isError ? (
                    <div className="space-y-3">
                      <p
                        role="alert"
                        className="text-sm text-destructive"
                      >
                        Nie udało się pobrać słownika.
                        Dotychczasowy wybór zostanie
                        zachowany.
                      </p>

                      <Button
                        type="button"
                        variant="outline"
                        disabled={
                          specializations.isFetching
                        }
                        onClick={() =>
                          void specializations.refetch()
                        }
                      >
                        Spróbuj ponownie
                      </Button>
                    </div>
                  ) : (
                    <div
                      role="group"
                      aria-labelledby="profile-specializations-label"
                      className="grid gap-3 sm:grid-cols-2"
                    >
                      {specializations.data.map(
                        (item) => (
                          <label
                            key={item.code}
                            className="flex cursor-pointer items-start gap-3 rounded-lg border bg-background p-3 text-sm"
                          >
                            <input
                              type="checkbox"
                              value={item.code}
                              className="mt-0.5 h-4 w-4 shrink-0 accent-primary"
                              aria-invalid={Boolean(
                                errors.specializations,
                              )}
                              aria-describedby={
                                errors.specializations
                                  ? 'profile-specializations-error'
                                  : undefined
                              }
                              {...register(
                                'specializations',
                              )}
                            />

                            <span>
                              {item.label}
                            </span>
                          </label>
                        ),
                      )}
                    </div>
                  )}

                  {errors.specializations && (
                    <p
                      id="profile-specializations-error"
                      role="alert"
                      className="text-sm text-destructive"
                    >
                      {errors.specializations.message}
                    </p>
                  )}
                </div>
              )}

              <div className="grid gap-5 sm:grid-cols-2">
                {profileFields
                  .filter(
                    (field) =>
                      field.section === section.id,
                  )
                  .map((field) => {
                    const id =
                      `profile-${field.name}`;
                    const registration =
                      register(field.name);

                    return (
                      <div
                        key={field.name}
                        className={`space-y-2 ${
                          field.multiline
                            ? 'sm:col-span-2'
                            : ''
                        }`}
                      >
                        <Label htmlFor={id}>
                          {field.label}
                          {field.name === 'firstName' ||
                          field.name === 'lastName'
                            ? ' *'
                            : ''}
                        </Label>

                        {field.multiline ? (
                          <Textarea
                            id={id}
                            rows={4}
                            maxLength={field.max}
                            aria-invalid={Boolean(
                              errors[field.name],
                            )}
                            aria-describedby={`${id}-help ${
                              errors[field.name]
                                ? `${id}-error`
                                : ''
                            }`}
                            {...registration}
                          />
                        ) : (
                          <Input
                            id={id}
                            type={
                              field.type ?? 'text'
                            }
                            maxLength={field.max}
                            aria-invalid={Boolean(
                              errors[field.name],
                            )}
                            aria-describedby={`${id}-help ${
                              errors[field.name]
                                ? `${id}-error`
                                : ''
                            }`}
                            {...registration}
                          />
                        )}

                        <p
                          id={`${id}-help`}
                          className="text-xs text-muted-foreground"
                        >
                          {field.name === 'postalCode'
                            ? 'Format 00-000.'
                            : `Maksymalnie ${field.max} znaków.`}
                        </p>

                        {errors[field.name] && (
                          <p
                            id={`${id}-error`}
                            role="alert"
                            className="text-sm text-destructive"
                          >
                            {
                              errors[field.name]
                                ?.message
                            }
                          </p>
                        )}
                      </div>
                    );
                  })}
              </div>
            </section>
          ))}

          <section className="space-y-4 rounded-2xl border bg-card p-5 sm:p-6">
            <h2 className="text-3xl">
              Widoczność profilu
            </h2>

            <label className="flex cursor-pointer items-start gap-3 rounded-xl border bg-primary/5 p-4 text-sm">
              <input
                type="checkbox"
                className="mt-1 h-4 w-4 shrink-0 accent-primary"
                {...register('isPublished')}
              />

              <span>
                <span className="block font-semibold">
                  Opublikuj profil w katalogu
                </span>

                <span className="mt-2 block leading-relaxed text-muted-foreground">
                  Opublikowany profil udostępnia dane
                  zawodowe, zdjęcie oraz wpisane dane
                  gabinetu i publicznego kontaktu.
                  Aby ukryć profil, odznacz tę opcję
                  i zapisz formularz.
                </span>
              </span>
            </label>

            <p className="text-sm text-muted-foreground">
              Do publikacji wymagane są: imię,
              nazwisko, miejscowość i nazwa gabinetu.
              Zdjęcie i nowe dane są opcjonalne.
            </p>

            {missing.length > 0 && (
              <p className="text-sm text-muted-foreground">
                Według ostatniego zapisu do publikacji
                brakuje: {missing.join(', ')}.
              </p>
            )}

            {errors.isPublished && (
              <p
                role="alert"
                className="text-sm text-destructive"
              >
                {errors.isPublished.message}
              </p>
            )}

            <p className="text-xs text-muted-foreground">
              Zapis kwalifikacji i specjalizacji nie
              oznacza ich niezależnej weryfikacji
              przez PhysioFlow.
            </p>
          </section>
        </fieldset>

        {mutation.isError && (
          <p
            role="alert"
            className="rounded-xl border border-destructive/30 bg-card p-4 text-sm text-destructive"
          >
            {getApiMessage(mutation.error)}
          </p>
        )}

        {saved && !isDirty && (
          <p
            role="status"
            className="rounded-xl border bg-primary/5 p-4 text-sm text-primary"
          >
            Profil został zapisany.
          </p>
        )}

        <div className="flex flex-col gap-3 rounded-2xl border bg-card p-4 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-xs text-muted-foreground">
            {isDirty
              ? 'Masz niezapisane zmiany.'
              : 'Dane formularza są zapisane.'}
          </p>

          <div className="flex flex-wrap gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={formBlocked || !isDirty}
              onClick={() =>
                confirmLeave(() => {
                  reset(profileToValues(profile));
                  mutation.reset();
                  setSaved(false);
                })
              }
            >
              <RotateCcw
                aria-hidden="true"
                className="mr-2 h-4 w-4"
              />
              Odrzuć zmiany
            </Button>

            <Button
              type="submit"
              disabled={formBlocked || !isDirty}
            >
              {mutation.isPending ? (
                <Loader2
                  aria-hidden="true"
                  className="mr-2 h-4 w-4 animate-spin"
                />
              ) : (
                <Save
                  aria-hidden="true"
                  className="mr-2 h-4 w-4"
                />
              )}

              {mutation.isPending
                ? 'Zapisywanie…'
                : 'Zapisz profil'}
            </Button>
          </div>
        </div>
      </form>

      {discardDialog}
    </>
  );
}