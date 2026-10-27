'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { Eye, EyeOff, HeartPulse, Loader2, LogIn } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
  Label,
} from '@/components/ui/primitives';
import { authApi } from '@/lib/api';
import { useAuthStore } from '@/stores/auth-store';

const REMEMBERED_EMAIL_KEY = 'physioflow-remembered-email';

const schema = z.object({
  email: z.string().email('Nieprawidłowy e-mail'),
  password: z.string().min(1, 'Wprowadź hasło'),
  rememberEmail: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

function readRememberedEmail(): string {
  if (typeof window === 'undefined') return '';

  try {
    return window.localStorage.getItem(REMEMBERED_EMAIL_KEY) ?? '';
  } catch {
    return '';
  }
}

export default function LoginPage() {
  const router = useRouter();
  const setTokens = useAuthStore((state) => state.setTokens);

  const [showPassword, setShowPassword] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isLoading },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: async () => {
      const email = readRememberedEmail();

      return {
        email,
        password: '',
        rememberEmail: email.length > 0,
      };
    },
  });

  const mutation = useMutation({
    mutationFn: async ({
      email,
      password,
      rememberEmail,
    }: FormValues) => {
      const tokens = await authApi.login(email, password);

      try {
        if (rememberEmail) {
          window.localStorage.setItem(REMEMBERED_EMAIL_KEY, email);
        } else {
          window.localStorage.removeItem(REMEMBERED_EMAIL_KEY);
        }
      } catch {
        // Brak dostępu do localStorage nie powinien blokować logowania.
      }

      return tokens;
    },
    onSuccess: (data) => {
      setTokens(data.accessToken, data.refreshToken);
      router.push('/');
    },
    onError: (error: AxiosError<{ error?: string }>) => {
      setApiError(
        error.response?.data?.error ?? 'Nieprawidłowy e-mail lub hasło'
      );
    },
  });

  const isBusy = isLoading || mutation.isPending;

  return (
    <Card className="border-border/60 shadow-2xl shadow-primary/5 backdrop-blur-sm">
      <CardHeader className="space-y-3 pb-4 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-gradient-to-br from-primary to-emerald-500 shadow-lg shadow-primary/30">
          <HeartPulse className="h-6 w-6 text-white" />
        </div>

        <div>
          <CardTitle className="text-2xl">
            Witaj w{' '}
            <span className="bg-gradient-to-r from-primary to-emerald-500 bg-clip-text text-transparent">
              PhysioFlow
            </span>
          </CardTitle>

          <CardDescription className="mt-1">
            Zaloguj się do swojego konta
          </CardDescription>
        </div>
      </CardHeader>

      <CardContent>
        <form
          onSubmit={handleSubmit((values) => {
            setApiError(null);
            mutation.mutate(values);
          })}
          className="space-y-4"
        >
          <div className="space-y-2">
            <Label htmlFor="email">E-mail</Label>

            <Input
              id="email"
              type="email"
              autoComplete="email"
              placeholder="adres@example.com"
              disabled={isBusy}
              {...register('email')}
            />

            {errors.email && (
              <p className="text-sm text-destructive">
                {errors.email.message}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label htmlFor="password">Hasło</Label>

              <button
                type="button"
                onClick={() => setShowPassword((value) => !value)}
                className="flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
                aria-label={showPassword ? 'Ukryj hasło' : 'Pokaż hasło'}
              >
                {showPassword ? (
                  <>
                    <EyeOff className="h-3.5 w-3.5" />
                    Ukryj
                  </>
                ) : (
                  <>
                    <Eye className="h-3.5 w-3.5" />
                    Pokaż
                  </>
                )}
              </button>
            </div>

            <Input
              id="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              placeholder="Wprowadź hasło"
              disabled={isBusy}
              {...register('password')}
            />

            {errors.password && (
              <p className="text-sm text-destructive">
                {errors.password.message}
              </p>
            )}
          </div>

          <label className="flex cursor-pointer select-none items-center gap-2 text-sm text-muted-foreground">
            <input
              type="checkbox"
              disabled={isBusy}
              className="h-4 w-4 cursor-pointer rounded border-input accent-primary"
              {...register('rememberEmail')}
            />
            Zapamiętaj mój e-mail
          </label>

          {apiError && (
            <div
              role="alert"
              className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive"
            >
              {apiError}
            </div>
          )}

          <Button type="submit" className="w-full" disabled={isBusy}>
            {isBusy ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                {isLoading ? 'Przygotowanie formularza...' : 'Logowanie...'}
              </>
            ) : (
              <>
                <LogIn className="mr-2 h-4 w-4" />
                Zaloguj się
              </>
            )}
          </Button>
        </form>

        <div className="mt-6 text-center text-sm text-muted-foreground">
          Nie masz jeszcze konta?{' '}
          <Link
            href="/register"
            className="font-medium text-primary hover:underline"
          >
            Załóż konto terapeuty
          </Link>
        </div>
      </CardContent>
    </Card>
  );
}