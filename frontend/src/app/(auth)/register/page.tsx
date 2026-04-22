'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label } from '@/components/ui/primitives';
import { authApi } from '@/lib/api';

const schema = z.object({
  firstName: z.string().min(2, 'Min. 2 znaki'),
  lastName: z.string().min(2, 'Min. 2 znaki'),
  email: z.string().email('Nieprawidłowy e-mail'),
  password: z.string().min(8, 'Min. 8 znaków'),
});

type FormValues = z.infer<typeof schema>;

export default function RegisterPage() {
  const router = useRouter();
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema) });

  const mutation = useMutation({
    mutationFn: authApi.register,
    onSuccess: () => {
      toast.success('Konto utworzone. Zaloguj się.');
      router.push('/login');
    },
    onError: () => toast.error('Rejestracja nie powiodła się'),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Rejestracja</CardTitle>
        <CardDescription>Utwórz konto terapeuty</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit((v) => mutation.mutate(v))} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="firstName">Imię</Label>
              <Input id="firstName" {...register('firstName')} />
              {errors.firstName && <p className="text-sm text-destructive">{errors.firstName.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="lastName">Nazwisko</Label>
              <Input id="lastName" {...register('lastName')} />
              {errors.lastName && <p className="text-sm text-destructive">{errors.lastName.message}</p>}
            </div>
          </div>
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
            {mutation.isPending ? 'Rejestracja...' : 'Utwórz konto'}
          </Button>
          <p className="text-center text-sm text-muted-foreground">
            Masz już konto?{' '}
            <Link href="/login" className="text-primary hover:underline">Zaloguj się</Link>
          </p>
        </form>
      </CardContent>
    </Card>
  );
}
