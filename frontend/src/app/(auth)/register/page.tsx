'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { Eye, EyeOff, HeartPulse, Info, Loader2, UserPlus } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
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

const schema = z.object({
  firstName: z.string().min(1, 'Imię jest wymagane').max(100, 'Maksymalnie 100 znaków'),
  lastName: z.string().min(1, 'Nazwisko jest wymagane').max(100, 'Maksymalnie 100 znaków'),
  email: z.string().email('Nieprawidłowy e-mail'),
  password: z.string().min(8, 'Hasło musi mieć min. 8 znaków'),
});

type FormValues = z.infer<typeof schema>;

export default function RegisterPage() {
  const router = useRouter();
  const setTokens = useAuthStore((s) => s.setTokens);
  const [showPassword, setShowPassword] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const mutation = useMutation({
    mutationFn: async (values: FormValues) => {
      await authApi.register(values);
      return authApi.login(values.email, values.password);
    },
    onSuccess: (tokens) => {
      setTokens(tokens.accessToken, tokens.refreshToken);
      toast.success('Konto utworzone');
      router.push('/');
    },
    onError: (error: AxiosError<{ errors?: string[]; error?: string }>) => {
      const data = error.response?.data;
      const message =
        (Array.isArray(data?.errors) && data.errors.length > 0 ? data.errors.join(' • ') : null) ??
        data?.error ??
        'Nie udało się utworzyć konta';
      setApiError(message);
    },
  });

  return (
    <Card className="border-border/60 shadow-2xl shadow-primary/5 backdrop-blur-sm">
      <CardHeader className="space-y-3 pb-4 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-gradient-to-br from-primary to-emerald-500 shadow-lg shadow-primary/30">
          <HeartPulse className="h-6 w-6 text-white" />
        </div>
        <div>
          <CardTitle className="text-2xl">
            Załóż konto{' '}
            <span className="bg-gradient-to-r from-primary to-emerald-500 bg-clip-text text-transparent">
              terapeuty
            </span>
          </CardTitle>
          <CardDescription className="mt-1">Zacznij korzystać z PhysioFlow w minutę</CardDescription>
        </div>
      </CardHeader>
      <CardContent>
        <form
          onSubmit={handleSubmit((v) => {
            setApiError(null);
            mutation.mutate(v);
          })}
          className="space-y-4"
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="firstName">Imię</Label>
              <Input id="firstName" autoComplete="given-name" {...register('firstName')} />
              {errors.firstName && (
                <p className="text-sm text-destructive">{errors.firstName.message}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="lastName">Nazwisko</Label>
              <Input id="lastName" autoComplete="family-name" {...register('lastName')} />
              {errors.lastName && (
                <p className="text-sm text-destructive">{errors.lastName.message}</p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="email">E-mail</Label>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              placeholder="adres@example.com"
              {...register('email')}
            />
            {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label htmlFor="password">Hasło</Label>
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                className="flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
              >
                {showPassword ? (
                  <>
                    <EyeOff className="h-3.5 w-3.5" /> Ukryj
                  </>
                ) : (
                  <>
                    <Eye className="h-3.5 w-3.5" /> Pokaż
                  </>
                )}
              </button>
            </div>
            <Input
              id="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="new-password"
              placeholder="Min. 8 znaków"
              {...register('password')}
            />
            {errors.password && (
              <p className="text-sm text-destructive">{errors.password.message}</p>
            )}
          </div>

          {apiError && (
            <div className="rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {apiError}
            </div>
          )}

          <div className="flex items-start gap-2 rounded-md border border-primary/20 bg-primary/5 p-3 text-xs text-muted-foreground">
            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0 text-primary" />
            <span>
              To konto jest przeznaczone dla <strong className="text-foreground">terapeutów</strong>.
              Pacjenci otrzymują dostęp do portalu poprzez zaproszenie wysłane przez swojego terapeutę.
            </span>
          </div>

          <Button type="submit" className="w-full" disabled={mutation.isPending}>
            {mutation.isPending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Tworzenie konta...
              </>
            ) : (
              <>
                <UserPlus className="mr-2 h-4 w-4" />
                Załóż konto
              </>
            )}
          </Button>
        </form>

        <div className="mt-6 text-center text-sm text-muted-foreground">
          Masz już konto?{' '}
          <Link href="/login" className="font-medium text-primary hover:underline">
            Zaloguj się
          </Link>
        </div>
      </CardContent>
    </Card>
  );
}