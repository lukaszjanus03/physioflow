'use client';

import axios from 'axios';
import { AlertCircle, Loader2, MailCheck } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Suspense, useState } from 'react';

import { Button } from '@/components/ui/button';

type ConfirmationResult =
  | { kind: 'confirmed' }
  | { kind: 'already-confirmed' }
  | { kind: 'error'; message: string };

const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

const cardClass =
  'rounded-2xl border bg-card p-6 text-center shadow-xl shadow-primary/5 sm:p-8';

const linkClass = 'inline-block text-sm font-medium text-primary hover:underline';

const userIdPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function ConfirmEmailForm() {
  const params = useSearchParams();
  const userId = params.get('userId');
  const token = params.get('token');

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [result, setResult] = useState<ConfirmationResult | null>(null);

  const hasValidParams = Boolean(
    userId && userIdPattern.test(userId) && token?.trim(),
  );

  async function confirmEmail() {
    if (!hasValidParams || isSubmitting) return;

    setIsSubmitting(true);
    setResult(null);

    try {
      const response = await axios.post<{ emailConfirmed: boolean }>(
        '/api/patient-accounts/confirm-email',
        { userId, token },
        { baseURL: apiUrl, timeout: 30_000 },
      );

      if (response.status === 200 && response.data?.emailConfirmed === true) {
        setResult({ kind: 'confirmed' });
      } else {
        setResult({
          kind: 'error',
          message: 'Serwer zwrócił nieoczekiwaną odpowiedź.',
        });
      }
    } catch (error) {
      const response = axios.isAxiosError<{ code?: string }>(error)
        ? error.response
        : undefined;

      if (
        response?.status === 409 &&
        response.data?.code === 'email_already_confirmed'
      ) {
        setResult({ kind: 'already-confirmed' });
        return;
      }

      const message =
        response?.status === 400
          ? 'Link jest nieprawidłowy lub wygasł. Możesz poprosić o nowy link.'
          : response?.status === 429
            ? 'Za dużo prób potwierdzenia. Odczekaj i spróbuj ponownie.'
            : !response
              ? 'Nie udało się odebrać odpowiedzi z serwera. Spróbuj ponownie.'
              : 'Nie udało się potwierdzić adresu e-mail. Spróbuj ponownie później.';

      setResult({ kind: 'error', message });
    } finally {
      setIsSubmitting(false);
    }
  }

  const confirmed =
    result?.kind === 'confirmed' || result?.kind === 'already-confirmed';
  const Icon = !hasValidParams || result?.kind === 'error' ? AlertCircle : MailCheck;

  return (
    <section className={cardClass} aria-busy={isSubmitting}>
      <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
        <Icon aria-hidden="true" className="h-6 w-6" />
      </div>

      {!hasValidParams ? (
        <>
          <h1 className="text-3xl">Nieprawidłowy link</h1>
          <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
            Otwórz pełny link z wiadomości potwierdzającej rejestrację lub
            poproś o nowy.
          </p>
        </>
      ) : confirmed ? (
        <div role="status">
          <h1 className="text-3xl">
            {result.kind === 'already-confirmed'
              ? 'Adres był już potwierdzony'
              : 'Adres e-mail potwierdzony'}
          </h1>
          <p className="mt-3 text-sm text-muted-foreground">
            Możesz teraz zalogować się do PhysioFlow.
          </p>
        </div>
      ) : (
        <>
          <h1 className="text-3xl">Potwierdź adres e-mail</h1>
          <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
            Potwierdź adres, aby móc zalogować się na konto pacjenta.
          </p>

          {result?.kind === 'error' && (
            <p
              role="alert"
              className="mt-4 rounded-xl border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
            >
              {result.message}
            </p>
          )}

          <Button
            type="button"
            className="mt-6 w-full"
            disabled={isSubmitting}
            onClick={confirmEmail}
          >
            {isSubmitting && (
              <Loader2 aria-hidden="true" className="mr-2 h-4 w-4 animate-spin" />
            )}
            {isSubmitting ? 'Potwierdzanie…' : 'Potwierdź adres e-mail'}
          </Button>
        </>
      )}

      <div className="mt-6 flex flex-col items-center gap-3">
        {!confirmed && (
          <Link href="/resend-confirmation" className={linkClass}>
            Poproś o nowy link
          </Link>
        )}

        <Link href="/login" className={linkClass}>
          Przejdź do logowania
        </Link>
      </div>
    </section>
  );
}

export default function ConfirmEmailPage() {
  return (
    <Suspense
      fallback={<p role="status" className={cardClass}>Wczytywanie linku…</p>}
    >
      <ConfirmEmailForm />
    </Suspense>
  );
}