'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label } from '@/components/ui/primitives';
import { authApi } from '@/lib/api';
import { useAuthStore } from '@/stores/auth-store';

const schema = z.object({
  email: z.string().email('Nieprawidłowy e-mail'),
  password: z.string().min(8, 'Hasło musi mieć min. 8 znaków'),
});

type FormValues = z.infer<typeof schema>;

export default function LoginPage() {
  const router = useRouter();
  const setTokens = useAuthStore((s) => s.setTokens);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const mutation = useMutation({
    mutationFn: ({ email, password }: FormValues) => authApi.login(email, password),
    onSuccess: (data) => {
      setTokens(data.accessToken, data.refreshToken);
      toast.success('Zalogowano');
      router.push('/');
    },
    onError: () => toast.error('Nieprawidłowy e-mail lub hasło'),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>PhysioFlow</CardTitle>
        <CardDescription>Zaloguj się do konta terapeuty</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit((v) => mutation.mutate(v))} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="email">E-mail</Label>
            <Input id="email" type="email" {...register('email')} />
            {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Hasło</Label>
            <Input id="password" type="password" {...register('password')} />
            {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
          </div>
          <Button type="submit" className="w-full" disabled={mutation.isPending}>
            {mutation.isPending ? 'Logowanie...' : 'Zaloguj się'}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
