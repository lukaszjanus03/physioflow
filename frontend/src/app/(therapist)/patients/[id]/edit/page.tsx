'use client';

import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import {
  ArrowLeft,
  ClipboardList,
  HeartPulse,
  IdCard,
  Mail,
  MapPin,
  Phone,
  Save,
  UserRound,
} from 'lucide-react';
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

  useEffect(() => {
    if (!patient) return;

    reset({
      firstName: patient.firstName,
      lastName: patient.lastName,
      pesel: patient.pesel ?? '',
      dateOfBirth: toDateInputValue(patient.dateOfBirth),
      email: patient.email ?? '',
      phoneNumber: patient.phoneNumber ?? '',
      address: patient.address ?? '',
      medicalHistory: patient.medicalHistory ?? '',
      medications: patient.medications ?? '',
      generalNotes: patient.generalNotes ?? '',
    });
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
    onSuccess: async () => {
      toast.success('Zapisano zmiany');

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['patient', id] }),
        queryClient.invalidateQueries({ queryKey: ['patients'] }),
      ]);

      router.push(`/patients/${id}`);
    },
    onError: (error: AxiosError<{ errors?: string[] | Record<string, string[]>; detail?: string; title?: string }>) => {
      toast.error(getApiErrorMessage(error, 'Nie udało się zapisać zmian'));
    },
  });

  if (isLoading) {
    return (
      <div className="mx-auto max-w-6xl p-6">
        <Card>
          <CardContent className="p-8 text-center text-muted-foreground">
            Ładowanie danych pacjenta...
          </CardContent>
        </Card>
      </div>
    );
  }

  if (isError || !patient) {
    return (
      <div className="mx-auto max-w-6xl p-6">
        <Card>
          <CardContent className="p-8 text-center text-destructive">
            Nie udało się załadować pacjenta.
            <div className="mt-4">
              <Button variant="outline" asChild>
                <Link href="/patients">Wróć do listy</Link>
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <Button variant="ghost" size="sm" asChild className="mb-4 w-fit">
            <Link href={`/patients/${id}`}>
              <ArrowLeft className="mr-2 h-4 w-4" />
              Wróć do karty pacjenta
            </Link>
          </Button>

          <div className="flex items-center gap-3">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <UserRound className="h-6 w-6" />
            </div>

            <div>
              <h1 className="text-3xl font-semibold tracking-tight">
                Edytuj pacjenta
              </h1>
              <p className="mt-1 text-muted-foreground">
                {patient.firstName} {patient.lastName}
              </p>
            </div>
          </div>
        </div>

        <div className="rounded-lg border bg-muted/30 px-4 py-3 text-sm text-muted-foreground">
          {isDirty ? 'Masz niezapisane zmiany.' : 'Brak niezapisanych zmian.'}
        </div>
      </div>

      <form onSubmit={handleSubmit((values) => mutation.mutate(values))} className="grid gap-6 lg:grid-cols-[1fr_320px]">
        <div className="space-y-6">
          <FormSection
            icon={<IdCard className="h-5 w-5" />}
            title="Dane podstawowe"
            description="Najważniejsze dane identyfikacyjne pacjenta."
          >
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="Imię" htmlFor="firstName" required error={errors.firstName?.message}>
                <Input id="firstName" autoComplete="given-name" {...register('firstName')} />
              </FormField>

              <FormField label="Nazwisko" htmlFor="lastName" required error={errors.lastName?.message}>
                <Input id="lastName" autoComplete="family-name" {...register('lastName')} />
              </FormField>

              <FormField label="PESEL" htmlFor="pesel" error={errors.pesel?.message}>
                <Input
                  id="pesel"
                  inputMode="numeric"
                  maxLength={11}
                  placeholder="11 cyfr"
                  {...register('pesel')}
                />
              </FormField>

              <FormField label="Data urodzenia" htmlFor="dateOfBirth" error={errors.dateOfBirth?.message}>
                <Input id="dateOfBirth" type="date" {...register('dateOfBirth')} />
              </FormField>
            </div>
          </FormSection>

          <FormSection
            icon={<Phone className="h-5 w-5" />}
            title="Kontakt"
            description="Dane przydatne do umawiania wizyt i kontaktu z pacjentem."
          >
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField label="E-mail" htmlFor="email" error={errors.email?.message}>
                <Input id="email" type="email" autoComplete="email" placeholder="pacjent@email.pl" {...register('email')} />
              </FormField>

              <FormField label="Telefon" htmlFor="phoneNumber" error={errors.phoneNumber?.message}>
                <Input
                  id="phoneNumber"
                  type="tel"
                  autoComplete="tel"
                  placeholder="np. 600 700 800"
                  {...register('phoneNumber')}
                />
              </FormField>
            </div>

            <FormField label="Adres" htmlFor="address">
              <Textarea
                id="address"
                rows={2}
                placeholder="Ulica, kod pocztowy, miasto"
                {...register('address')}
              />
            </FormField>
          </FormSection>

          <FormSection
            icon={<HeartPulse className="h-5 w-5" />}
            title="Informacje medyczne"
            description="Krótki kontekst terapeutyczny, który będzie widoczny w karcie pacjenta."
          >
            <FormField label="Historia medyczna" htmlFor="medicalHistory">
              <Textarea
                id="medicalHistory"
                rows={4}
                placeholder="Przebyte urazy, operacje, choroby przewlekłe..."
                {...register('medicalHistory')}
              />
            </FormField>

            <FormField label="Aktualne leki" htmlFor="medications">
              <Textarea
                id="medications"
                rows={3}
                placeholder="Lista przyjmowanych leków"
                {...register('medications')}
              />
            </FormField>

            <FormField label="Notatki ogólne" htmlFor="generalNotes">
              <Textarea
                id="generalNotes"
                rows={3}
                placeholder="Dodatkowe informacje, preferencje pacjenta..."
                {...register('generalNotes')}
              />
            </FormField>
          </FormSection>
        </div>

        <aside className="space-y-4">
          <Card className="sticky top-6">
            <CardHeader>
              <CardTitle className="text-lg">Zapis zmian</CardTitle>
              <CardDescription>
                Po zapisaniu wrócisz do szczegółów pacjenta.
              </CardDescription>
            </CardHeader>

            <CardContent className="space-y-4">
              <SummaryItem icon={<IdCard className="h-4 w-4" />} text="Dane podstawowe" />
              <SummaryItem icon={<Mail className="h-4 w-4" />} text="Kontakt i adres" />
              <SummaryItem icon={<ClipboardList className="h-4 w-4" />} text="Informacje medyczne" />
              <SummaryItem icon={<MapPin className="h-4 w-4" />} text={isDirty ? 'Zmiany oczekują na zapis' : 'Wszystko zapisane'} />

              <div className="space-y-2 border-t pt-4">
                <Button type="submit" disabled={mutation.isPending || !isDirty} className="w-full">
                  <Save className="mr-2 h-4 w-4" />
                  {mutation.isPending ? 'Zapisywanie...' : 'Zapisz zmiany'}
                </Button>

                <Button type="button" variant="outline" asChild className="w-full">
                  <Link href={`/patients/${id}`}>Anuluj</Link>
                </Button>
              </div>
            </CardContent>
          </Card>
        </aside>
      </form>
    </div>
  );
}

function FormSection({
  icon,
  title,
  description,
  children,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            {icon}
          </div>

          <div>
            <CardTitle className="text-lg">{title}</CardTitle>
            <CardDescription>{description}</CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">{children}</CardContent>
    </Card>
  );
}

function FormField({
  label,
  htmlFor,
  required = false,
  error,
  children,
}: {
  label: string;
  htmlFor: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={htmlFor}>
        {label} {required && <span className="text-destructive">*</span>}
      </Label>
      {children}
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  );
}

function SummaryItem({ icon, text }: { icon: React.ReactNode; text: string }) {
  return (
    <div className="flex items-center gap-3 text-sm text-muted-foreground">
      <div className="flex h-8 w-8 items-center justify-center rounded-md bg-muted text-foreground">
        {icon}
      </div>
      <span>{text}</span>
    </div>
  );
}

function toDateInputValue(value: string | null) {
  if (!value) return '';
  return value.slice(0, 10);
}

function getApiErrorMessage(
  error: AxiosError<{ errors?: string[] | Record<string, string[]>; detail?: string; title?: string }>,
  fallback: string,
) {
  const data = error.response?.data;

  if (data?.errors) {
    const flat = Array.isArray(data.errors) ? data.errors : Object.values(data.errors).flat();
    if (flat.length > 0) return flat.join(' • ');
  }

  return data?.detail ?? data?.title ?? fallback;
}
