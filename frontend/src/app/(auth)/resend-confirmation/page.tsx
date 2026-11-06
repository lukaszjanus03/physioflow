'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import axios from 'axios';
import { Loader2, Mail } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';

const schema = z.object({
  email: z
    .string()
    .trim()
    .email('Podaj poprawny adres e-mail')
    .max(256, 'Adres e-mail jest za długi'),
});

type FormValues = z.infer<typeof schema>;

const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

export default function ResendConfirmationPage() {
  const [accepted, setAccepted] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '' },
  });

  async function resendConfirmation({ email }: FormValues) {
    try {
      const response = await axios.post(
        '/api/patient-accounts/resend-confirmation',
        { email },
        { baseURL: apiUrl, timeout: 30_000 },
      );

      if (response.status !== 202) {
        setError('root', { message: 'Serwer zwrócił nieoczekiwaną odpowiedź.' });
        return;
      }

      setAccepted(true);
    } catch (error) {
      const response = axios.isAxiosError(error) ? error.response : undefined;

      if (response?.status === 400) {
        setError('email', {
          type: 'server',
          message: 'Sprawdź adres e-mail i spróbuj ponownie.',
        });
        return;
      }

      setError('root', {
        message:
          response?.status === 429
            ? 'Za dużo prób. Odczekaj i spróbuj ponownie.'
            : !response
              ? 'Nie udało się odebrać odpowiedzi z serwera. Sprawdź połączenie i skrzynkę przed ponowieniem próby.'
              : 'Nie udało się przyjąć prośby. Spróbuj ponownie później.',
      });
    }
  }

  return (
    <section
      className="rounded-2xl border bg-card p-6 shadow-xl shadow-primary/5 sm:p-8"
      aria-busy={isSubmitting}
    >
      <div className="mb-6 text-center">
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <Mail aria-hidden="true" className="h-6 w-6" />
        </div>

        <h1 className="text-3xl">
          {accepted ? 'Sprawdź swoją pocztę' : 'Poproś o nowy link'}
        </h1>

        {!accepted && (
          <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
            Podaj adres e-mail użyty podczas rejestracji konta pacjenta.
          </p>
        )}
      </div>

      {accepted ? (
        <p role="status" className="text-center text-sm leading-relaxed text-muted-foreground">
          Prośba została przyjęta. Jeśli konto wymaga potwierdzenia i wysyłka
          jest możliwa, otrzymasz nowy link. Sprawdź skrzynkę oraz folder spam.
        </p>
      ) : (
        <form
          noValidate
          onSubmit={handleSubmit(resendConfirmation)}
          className="space-y-4"
        >
          <div className="space-y-2">
            <Label htmlFor="resend-email">E-mail</Label>

            <Input
              id="resend-email"
              type="email"
              autoComplete="email"
              maxLength={256}
              required
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.email)}
              aria-describedby={errors.email ? 'resend-email-error' : undefined}
              {...register('email')}
            />

            {errors.email && (
              <p id="resend-email-error" role="alert" className="text-sm text-destructive">
                {errors.email.message}
              </p>
            )}
          </div>

          {errors.root?.message && (
            <p
              role="alert"
              className="rounded-xl border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
            >
              {errors.root.message}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting && (
              <Loader2 aria-hidden="true" className="mr-2 h-4 w-4 animate-spin" />
            )}
            {isSubmitting ? 'Przetwarzanie…' : 'Poproś o nowy link'}
          </Button>
        </form>
      )}

      <p className="mt-6 text-center">
        <Link href="/login" className="text-sm font-medium text-primary hover:underline">
          Przejdź do logowania
        </Link>
      </p>
    </section>
  );
}