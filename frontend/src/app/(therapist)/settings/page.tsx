'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Save } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';

import { Button } from '@/components/ui/button';
import { Input, Label, Textarea } from '@/components/ui/primitives';
import {
  getApiMessage,
  portalApi,
  type ApiProblem,
  type TherapistOwnProfile,
  type TherapistProfilePayload,
} from '@/lib/portal-api';
import { useAuthStore } from '@/stores/auth-store';

const fields = [
  { name: 'firstName', label: 'Imię', maxLength: 100 },
  { name: 'lastName', label: 'Nazwisko', maxLength: 100 },
  { name: 'city', label: 'Miasto', maxLength: 100 },
  { name: 'practiceName', label: 'Nazwa gabinetu', maxLength: 150 },
] as const;

export default function SettingsPage() {
  const { userId, roles, isAuthenticated } = useAuthStore();
  const enabled = isAuthenticated && roles.includes('Therapist') && Boolean(userId);

  const query = useQuery({
    queryKey: ['therapist-own-profile', userId],
    queryFn: ({ signal }) => portalApi.myTherapistProfile(signal),
    enabled,
    refetchOnWindowFocus: false,
  });

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-4 sm:p-6">
      <div>
        <h1 className="text-4xl">Ustawienia</h1>
        <p className="mt-3 text-sm text-muted-foreground">
          Uzupełnij profil zawodowy i zdecyduj, czy ma być widoczny dla pacjentów.
        </p>
      </div>

      {!enabled || query.isPending ? (
        <p role="status">Wczytywanie profilu…</p>
      ) : query.isError ? (
        <div className="rounded-xl border bg-card p-6">
          <p role="alert" className="text-sm text-destructive">
            {getApiMessage(query.error)}
          </p>
          <Button
            type="button"
            variant="outline"
            className="mt-4"
            disabled={query.isFetching}
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </div>
      ) : (
        <ProfileForm
          key={`${userId}:${query.data.id}`}
          profile={query.data}
          accountId={userId!}
        />
      )}
    </div>
  );
}

function ProfileForm({
  profile,
  accountId,
}: {
  profile: TherapistOwnProfile;
  accountId: string;
}) {
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<TherapistProfilePayload>({
    defaultValues: {
      firstName: profile.firstName,
      lastName: profile.lastName,
      city: profile.city ?? '',
      practiceName: profile.practiceName ?? '',
      bio: profile.bio ?? '',
      isPublished: profile.isPublished,
    },
  });

  const mutation = useMutation({
    mutationFn: portalApi.updateTherapistProfile,
    onSuccess: (data) => {
      reset({
        firstName: data.firstName,
        lastName: data.lastName,
        city: data.city ?? '',
        practiceName: data.practiceName ?? '',
        bio: data.bio ?? '',
        isPublished: data.isPublished,
      });

      queryClient.setQueryData(['therapist-own-profile', accountId], data);
      void queryClient.invalidateQueries({ queryKey: ['patient-catalog'] });
      void queryClient.invalidateQueries({ queryKey: ['patient-therapist'] });
      setSaved(true);
    },
    onError: (error) => {
      if (isAxiosError<ApiProblem>(error)) {
        const serverErrors = error.response?.data?.errors;

        for (const name of [
          'firstName',
          'lastName',
          'city',
          'practiceName',
          'bio',
          'isPublished',
        ] as const) {
          const messages = serverErrors?.[name];

          if (Array.isArray(messages) && messages.length > 0) {
            setError(name, {
              type: 'server',
              message: messages.join(' '),
            });
          }
        }
      }
    },
  });

  function onSubmit(values: TherapistProfilePayload) {
    setSaved(false);

    const payload = {
      ...values,
      firstName: values.firstName.trim(),
      lastName: values.lastName.trim(),
      city: values.city.trim(),
      practiceName: values.practiceName.trim(),
      bio: values.bio.trim(),
    };

    let valid = true;

    if (payload.isPublished) {
      if (!payload.city) {
        setError('city', { message: 'Miasto jest wymagane do publikacji.' });
        valid = false;
      }

      if (!payload.practiceName) {
        setError('practiceName', {
          message: 'Nazwa gabinetu jest wymagana do publikacji.',
        });
        valid = false;
      }

      if (payload.lastName === '(uzupełnij)') {
        setError('lastName', {
          message: 'Uzupełnij nazwisko przed publikacją.',
        });
        valid = false;
      }
    }

    if (valid) mutation.mutate(payload);
  }

  const current = mutation.data ?? profile;

  return (
    <form
      noValidate
      onSubmit={handleSubmit(onSubmit)}
      aria-busy={mutation.isPending}
      className="rounded-2xl border bg-card p-6"
    >
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-3xl">Profil zawodowy</h2>
        <span className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
          {current.isPublished ? 'Opublikowany' : 'Nieopublikowany'}
        </span>
      </div>

      <fieldset disabled={mutation.isPending} className="min-w-0 space-y-5">
        <div className="grid gap-5 sm:grid-cols-2">
          {fields.map((field) => (
            <div key={field.name} className="space-y-2">
              <Label htmlFor={`profile-${field.name}`}>{field.label}</Label>
              <Input
                id={`profile-${field.name}`}
                maxLength={field.maxLength}
                aria-invalid={Boolean(errors[field.name])}
                aria-describedby={errors[field.name] ? `${field.name}-error` : undefined}
                {...register(field.name, {
                  validate: (value) =>
                    field.name === 'firstName' || field.name === 'lastName'
                      ? value.trim().length > 0 || 'To pole jest wymagane.'
                      : true,
                })}
              />

              {errors[field.name] && (
                <p id={`${field.name}-error`} className="text-sm text-destructive">
                  {errors[field.name]?.message}
                </p>
              )}
            </div>
          ))}
        </div>

        <div className="space-y-2">
          <Label htmlFor="profile-bio">Opis zawodowy</Label>
          <Textarea
            id="profile-bio"
            rows={5}
            maxLength={500}
            placeholder="Krótko opisz obszar swojej pracy."
            aria-invalid={Boolean(errors.bio)}
            aria-describedby={errors.bio ? 'bio-error' : undefined}
            {...register('bio')}
          />
          <p className="text-xs text-muted-foreground">Maksymalnie 500 znaków.</p>
          {errors.bio && (
            <p id="bio-error" className="text-sm text-destructive">
              {errors.bio.message}
            </p>
          )}
        </div>

        <div className="rounded-xl border bg-muted/30 p-4">
          <label className="flex items-start gap-3 text-sm">
            <input
              type="checkbox"
              className="mt-1 h-4 w-4 accent-primary"
              {...register('isPublished')}
            />
            <span>
              <span className="block font-medium">Opublikuj profil w katalogu</span>
              <span className="mt-2 block leading-relaxed text-muted-foreground">
                Pacjenci zobaczą Twoje imię, nazwisko, miasto, nazwę gabinetu i opis.
                Aby ukryć profil, odznacz tę opcję i zapisz zmiany.
              </span>
            </span>
          </label>

          {errors.isPublished && (
            <p className="mt-2 text-sm text-destructive">
              {errors.isPublished.message}
            </p>
          )}
        </div>

        {mutation.isError && (
          <p role="alert" className="text-sm text-destructive">
            {getApiMessage(mutation.error)}
          </p>
        )}

        {saved && !isDirty && (
          <p role="status" className="text-sm text-primary">
            Profil został zapisany.
          </p>
        )}

        <Button type="submit" disabled={mutation.isPending}>
          <Save aria-hidden="true" className="mr-2 h-4 w-4" />
          {mutation.isPending ? 'Zapisywanie…' : 'Zapisz zmiany'}
        </Button>

        <p className="text-xs leading-relaxed text-muted-foreground">
          Zmiana widoczności zacznie obowiązywać po zapisaniu formularza.
          Sam zapis nie publikuje profilu, jeśli opcja publikacji jest wyłączona.
        </p>
      </fieldset>
    </form>
  );
}