'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useEffect } from 'react';
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
  Textarea,
} from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import type { UpdatePatientPayload } from '@/types/api';

const schema = z.object({
  firstName: z.string().min(1, 'Imię jest wymagane').max(100, 'Maksymalnie 100 znaków'),
  lastName: z.string().min(1, 'Nazwisko jest wymagane').max(100, 'Maksymalnie 100 znaków'),
  pesel: z
    .string()
    .regex(/^\d{11}$/, 'PESEL musi mieć dokładnie 11 cyfr')
    .optional()
    .or(z.literal('')),
  dateOfBirth: z.string().optional().or(z.literal('')),
  email: z.string().email('Nieprawidłowy e-mail').optional().or(z.literal('')),
  phoneNumber: z.string().max(20, 'Maksymalnie 20 znaków').optional().or(z.literal('')),
  address: z.string().optional().or(z.literal('')),
  medicalHistory: z.string().optional().or(z.literal('')),
  medications: z.string().optional().or(z.literal('')),
  generalNotes: z.string().optional().or(z.literal('')),
});

type FormValues = z.infer<typeof schema>;

export default function EditPatientPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const queryClient = useQueryClient();

  const {
    data: patient,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['patient', id],
    queryFn: () => patientsApi.getById(id!),
    enabled: !!id,
  });

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: '',
      lastName: '',
      pesel: '',
      dateOfBirth: '',
      email: '',
      phoneNumber: '',
      address: '',
      medicalHistory: '',
      medications: '',
      generalNotes: '',
    },
  });

  // Po załadowaniu pacjenta wpisujemy jego dane do formularza.
  useEffect(() => {
    if (patient) {
      reset({
        firstName: patient.firstName,
        lastName: patient.lastName,
        pesel: patient.pesel ?? '',
        dateOfBirth: patient.dateOfBirth ?? '',
        email: patient.email ?? '',
        phoneNumber: patient.phoneNumber ?? '',
        address: patient.address ?? '',
        medicalHistory: patient.medicalHistory ?? '',
        medications: patient.medications ?? '',
        generalNotes: patient.generalNotes ?? '',
      });
    }
  }, [patient, reset]);

  const mutation = useMutation({
    mutationFn: (values: FormValues) => {
      const payload: UpdatePatientPayload = {
        id: id!,
        firstName: values.firstName.trim(),
        lastName: values.lastName.trim(),
        pesel: values.pesel?.trim() || undefined,
        dateOfBirth: values.dateOfBirth?.trim() || undefined,
        email: values.email?.trim() || undefined,
        phoneNumber: values.phoneNumber?.trim() || undefined,
        address: values.address?.trim() || undefined,
        medicalHistory: values.medicalHistory?.trim() || undefined,
        medications: values.medications?.trim() || undefined,
        generalNotes: values.generalNotes?.trim() || undefined,
      };
      return patientsApi.update(id!, payload);
    },
    onSuccess: () => {
      toast.success('Zapisano zmiany');
      queryClient.invalidateQueries({ queryKey: ['patient', id] });
      queryClient.invalidateQueries({ queryKey: ['patients'] });
      router.push(`/patients/${id}`);
    },
    onError: (error: AxiosError<{ errors?: string[] | Record<string, string[]>; detail?: string; title?: string }>) => {
      const data = error.response?.data;
      let message = 'Nie udało się zapisać zmian';
      if (data?.errors) {
        const flat = Array.isArray(data.errors) ? data.errors : Object.values(data.errors).flat();
        if (flat.length > 0) message = flat.join(' • ');
      } else if (data?.detail) {
        message = data.detail;
      } else if (data?.title) {
        message = data.title;
      }
      toast.error(message);
    },
  });

  if (isLoading) {
    return <div className="p-6 text-muted-foreground">Ładowanie...</div>;
  }

  if (isError || !patient) {
    return (
      <div className="p-6 text-destructive">
        Nie udało się załadować pacjenta.{' '}
        <Link href="/patients" className="underline">
          Wróć do listy
        </Link>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/patients/${id}`}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Wróć
          </Link>
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>
            Edytuj: {patient.firstName} {patient.lastName}
          </CardTitle>
          <CardDescription>Zmień dane pacjenta. Pola oznaczone * są wymagane.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit((values) => mutation.mutate(values))} className="space-y-6">
            <section className="space-y-4">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
                Dane podstawowe
              </h2>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="firstName">Imię *</Label>
                  <Input id="firstName" {...register('firstName')} />
                  {errors.firstName && <p className="text-sm text-destructive">{errors.firstName.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="lastName">Nazwisko *</Label>
                  <Input id="lastName" {...register('lastName')} />
                  {errors.lastName && <p className="text-sm text-destructive">{errors.lastName.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="pesel">PESEL</Label>
                  <Input id="pesel" inputMode="numeric" maxLength={11} {...register('pesel')} />
                  {errors.pesel && <p className="text-sm text-destructive">{errors.pesel.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="dateOfBirth">Data urodzenia</Label>
                  <Input id="dateOfBirth" type="date" {...register('dateOfBirth')} />
                </div>
              </div>
            </section>

            <section className="space-y-4">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
                Kontakt
              </h2>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="email">E-mail</Label>
                  <Input id="email" type="email" {...register('email')} />
                  {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="phoneNumber">Telefon</Label>
                  <Input id="phoneNumber" type="tel" {...register('phoneNumber')} />
                  {errors.phoneNumber && <p className="text-sm text-destructive">{errors.phoneNumber.message}</p>}
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="address">Adres</Label>
                <Textarea id="address" rows={2} {...register('address')} />
              </div>
            </section>

            <section className="space-y-4">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
                Informacje medyczne
              </h2>
              <div className="space-y-2">
                <Label htmlFor="medicalHistory">Historia medyczna</Label>
                <Textarea id="medicalHistory" rows={4} {...register('medicalHistory')} />
              </div>
              <div className="space-y-2">
                <Label htmlFor="medications">Aktualne leki</Label>
                <Textarea id="medications" rows={3} {...register('medications')} />
              </div>
              <div className="space-y-2">
                <Label htmlFor="generalNotes">Notatki ogólne</Label>
                <Textarea id="generalNotes" rows={3} {...register('generalNotes')} />
              </div>
            </section>

            <div className="flex justify-end gap-3 border-t pt-6">
              <Button type="button" variant="outline" asChild>
                <Link href={`/patients/${id}`}>Anuluj</Link>
              </Button>
              <Button type="submit" disabled={mutation.isPending || !isDirty}>
                {mutation.isPending ? 'Zapisywanie...' : 'Zapisz zmiany'}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}