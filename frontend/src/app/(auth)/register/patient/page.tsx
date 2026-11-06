'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import axios from 'axios';
import { ArrowLeft, Loader2, MailCheck, UserRound } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';

const schema = z
  .object({
    firstName: z.string().trim().min(1, 'Podaj imię').max(100, 'Maksymalnie 100 znaków'),
    lastName: z.string().trim().min(1, 'Podaj nazwisko').max(100, 'Maksymalnie 100 znaków'),
    email: z.string().trim().email('Podaj poprawny adres e-mail').max(256, 'Adres e-mail jest za długi'),
    password: z
      .string()
      .min(8, 'Hasło musi mieć co najmniej 8 znaków')
      .max(128, 'Hasło może mieć maksymalnie 128 znaków')
      .regex(/[a-z]/, 'Dodaj małą literę')
      .regex(/[A-Z]/, 'Dodaj wielką literę')
      .regex(/[0-9]/, 'Dodaj cyfrę'),
    confirmPassword: z.string().min(1, 'Powtórz hasło'),
  })
  .refine(({ password, confirmPassword }) => password === confirmPassword, {
    message: 'Hasła muszą być takie same',
    path: ['confirmPassword'],
  });

type FormValues = z.infer<typeof schema>;

type RegistrationResponse = {
  userId: string;
  email: string;
  emailConfirmationRequired: true;
};

type RegistrationError = {
  errors?: Partial<Record<keyof FormValues, string[]>>;
};

const fields = [
  { name: 'firstName', label: 'Imię', type: 'text', autoComplete: 'given-name', maxLength: 100 },
  { name: 'lastName', label: 'Nazwisko', type: 'text', autoComplete: 'family-name', maxLength: 100 },
  { name: 'email', label: 'E-mail', type: 'email', autoComplete: 'email', maxLength: 256 },
  { name: 'password', label: 'Hasło', type: 'password', autoComplete: 'new-password', maxLength: 128 },
  { name: 'confirmPassword', label: 'Powtórz hasło', type: 'password', autoComplete: 'new-password', maxLength: 128 },
] as const;

// Publiczna rejestracja nie wymaga tokenu sesji ani jego odświeżania.
const registrationClient = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000',
  timeout: 30_000,
});

export default function PatientRegisterPage() {
  const [showPassword, setShowPassword] = useState(false);
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setError,
    clearErrors,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      confirmPassword: '',
    },
  });

  async function onSubmit({ firstName, lastName, email, password }: FormValues) {
    clearErrors('root');

    try {
      const response = await registrationClient.post<RegistrationResponse>(
        '/api/patient-accounts/register',
        { firstName, lastName, email, password },
      );

      if (response.status !== 201 || response.data?.emailConfirmationRequired !== true) {
        setError('root', { message: 'Serwer zwrócił nieoczekiwaną odpowiedź.' });
        return;
      }

      setRegisteredEmail(response.data.email || email);
      reset();
    } catch (error) {
      if (!axios.isAxiosError<RegistrationError>(error)) {
        setError('root', { message: 'Nie udało się zarejestrować konta.' });
        return;
      }

      const status = error.response?.status;

      if (status === 400) {
        let hasFieldErrors = false;

        for (const field of fields) {
          const messages = error.response?.data?.errors?.[field.name];

          if (Array.isArray(messages) && messages.length > 0) {
            setError(field.name, { type: 'server', message: messages.join(' ') });
            hasFieldErrors = true;
          }
        }

        setError('root', {
          message: hasFieldErrors
            ? 'Popraw zaznaczone pola i spróbuj ponownie.'
            : 'Serwer odrzucił dane formularza. Sprawdź je i spróbuj ponownie.',
        });
        return;
      }

      if (status === 409) {
        setError('email', { type: 'server', message: 'Ten adres e-mail jest już zarejestrowany.' });
        return;
      }

      const message =
        status === 429
          ? 'Za dużo prób rejestracji. Odczekaj i spróbuj ponownie.'
          : status === 503
            ? 'Nie udało się wysłać wiadomości potwierdzającej. Spróbuj ponownie później.'
            : !error.response
              ? 'Nie udało się odebrać odpowiedzi z serwera. Sprawdź połączenie i skrzynkę, zanim ponowisz próbę.'
              : 'Nie udało się zarejestrować konta. Spróbuj ponownie później.';

      setError('root', { message });
    }
  }

  if (registeredEmail) {
    return (
      <section className="rounded-2xl border bg-card p-6 text-center shadow-xl shadow-primary/5 sm:p-8">
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <MailCheck aria-hidden="true" className="h-6 w-6" />
        </div>

        <div role="status">
          <h1 className="text-3xl">Sprawdź swoją pocztę</h1>
          <p className="mt-3 break-words text-sm leading-relaxed text-muted-foreground">
            Konto zostało utworzone, a wiadomość potwierdzającą przekazano do wysyłki
            na adres <strong className="text-foreground">{registeredEmail}</strong>.
          </p>
        </div>

        <p className="mt-3 text-sm text-muted-foreground">
          Jeśli nie widzisz wiadomości, sprawdź folder spam.
        </p>

        <Link href="/login" className="mt-6 inline-block text-sm font-medium text-primary hover:underline">
          Przejdź do logowania
        </Link>
      </section>
    );
  }

  return (
    <div className="rounded-2xl border bg-card p-6 shadow-xl shadow-primary/5 sm:p-8">
      <Link href="/register" className="mb-6 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary">
        <ArrowLeft aria-hidden="true" className="h-4 w-4" />
        Wybierz rodzaj konta
      </Link>

      <div className="mb-6 text-center">
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary text-primary-foreground">
          <UserRound aria-hidden="true" className="h-6 w-6" />
        </div>
        <h1 className="text-3xl">Konto pacjenta</h1>
        <p className="mt-3 text-sm text-muted-foreground">
          Podaj dane, aby założyć konto w PhysioFlow.
        </p>
      </div>

      <form noValidate onSubmit={handleSubmit(onSubmit)} aria-busy={isSubmitting}>
        <fieldset disabled={isSubmitting} className="min-w-0 space-y-4">
          {fields.map((field) => {
            const error = errors[field.name];
            const hintId = field.name === 'password' ? 'password-hint' : undefined;
            const errorId = `${field.name}-error`;
            const describedBy = [hintId, error ? errorId : undefined].filter(Boolean).join(' ');

            return (
              <div key={field.name} className="space-y-2">
                <Label htmlFor={field.name}>{field.label}</Label>
                <Input
                  id={field.name}
                  type={showPassword && field.type === 'password' ? 'text' : field.type}
                  autoComplete={field.autoComplete}
                  maxLength={field.maxLength}
                  required
                  aria-invalid={Boolean(error)}
                  aria-describedby={describedBy || undefined}
                  {...register(field.name)}
                />

                {hintId && (
                  <p id={hintId} className="text-xs leading-relaxed text-muted-foreground">
                    Od 8 do 128 znaków, w tym mała i wielka litera oraz cyfra.
                  </p>
                )}

                {error && (
                  <p id={errorId} role="alert" className="text-sm text-destructive">
                    {error.message}
                  </p>
                )}
              </div>
            );
          })}

          <button
            type="button"
            onClick={() => setShowPassword((value) => !value)}
            className="rounded-md py-1 text-sm text-muted-foreground hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            {showPassword ? 'Ukryj hasła' : 'Pokaż hasła'}
          </button>

          {errors.root?.message && (
            <p role="alert" className="rounded-xl border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
              {errors.root.message}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting && <Loader2 aria-hidden="true" className="mr-2 h-4 w-4 animate-spin" />}
            {isSubmitting ? 'Tworzenie konta…' : 'Załóż konto'}
          </Button>
        </fieldset>
      </form>

      <p className="mt-6 text-center text-sm text-muted-foreground">
        Masz już konto?{' '}
        <Link href="/login" className="font-medium text-primary hover:underline">
          Zaloguj się
        </Link>
      </p>
    </div>
  );
}