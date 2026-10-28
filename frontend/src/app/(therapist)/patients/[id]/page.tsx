'use client';

import { useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { toast } from 'sonner';
import {
  Activity,
  AlertTriangle,
  Archive,
  ArrowLeft,
  Calendar,
  Cake,
  ClipboardList,
  Clock,
  Dumbbell,
  FileDown,
  FileText,
  HeartPulse,
  IdCard,
  Mail,
  MapPin,
  NotebookText,
  Pencil,
  Phone,
  Pill,
  RotateCcw,
  ShieldCheck,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import { cn, formatDate, formatDateTime } from '@/lib/utils';
import type { PatientDetail } from '@/types/api';

export default function PatientDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const queryClient = useQueryClient();

  const [archiveDialogOpen, setArchiveDialogOpen] = useState(false);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['patient', id],
    queryFn: () => patientsApi.getById(id!),
    enabled: !!id,
  });

  const archiveMutation = useMutation({
    mutationFn: () => patientsApi.archive(id!),
    onSuccess: async () => {
      toast.success('Pacjent zarchiwizowany');
      setArchiveDialogOpen(false);

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['patients'] }),
        queryClient.invalidateQueries({ queryKey: ['patient', id] }),
      ]);

      router.push('/patients');
    },
    onError: (mutationError: AxiosError<{ detail?: string; title?: string }>) => {
      const message =
        mutationError.response?.data?.detail
        ?? mutationError.response?.data?.title
        ?? 'Nie udało się zarchiwizować pacjenta';

      toast.error(message);
    },
  });

  const unarchiveMutation = useMutation({
    mutationFn: () => patientsApi.unarchive(id!),
    onSuccess: async () => {
      toast.success('Pacjent przywrócony z archiwum');

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['patients'] }),
        queryClient.invalidateQueries({ queryKey: ['patient', id] }),
      ]);
    },
    onError: (mutationError: AxiosError<{ detail?: string; title?: string }>) => {
      const message =
        mutationError.response?.data?.detail
        ?? mutationError.response?.data?.title
        ?? 'Nie udało się przywrócić pacjenta';

      toast.error(message);
    },
  });

  const exportPdfMutation = useMutation({
  mutationFn: async () => {
    if (!data) {
      throw new Error('Brak danych pacjenta.');
    }

    const patient = data;

    const { downloadPatientCardPdf } = await import(
      '@/components/patients/PatientCardPdf'
    );

    await downloadPatientCardPdf(patient);
  },
  onSuccess: () => {
    toast.success('Przygotowano plik PDF do pobrania');
  },
  onError: () => {
    toast.error(
      'Nie udało się wygenerować PDF. Sprawdź, czy pliki czcionek są dostępne, i spróbuj ponownie.'
    );
  },
});

  const isMutating = archiveMutation.isPending || unarchiveMutation.isPending;

  const handleArchive = () => {
    if (!id || isMutating || !data || data.isArchived) return;
    setArchiveDialogOpen(true);
  };

  const handleConfirmArchive = () => {
    if (!id || isMutating || !data || data.isArchived) return;
    archiveMutation.mutate();
  };

  const handleUnarchive = () => {
    if (!id || isMutating || !data || !data.isArchived) return;
    unarchiveMutation.mutate();
  };

  const handleExportPdf = () => {
  if (!data || exportPdfMutation.isPending) return;

  exportPdfMutation.mutate();
};

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <Button variant="ghost" size="sm" asChild className="w-fit">
          <Link href="/patients">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Wróć do listy
          </Link>
        </Button>

        {data && (
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <Button variant="outline" onClick={handleExportPdf} disabled={exportPdfMutation.isPending}>
              <FileDown className="mr-2 h-4 w-4" />
              {exportPdfMutation.isPending ? 'Eksportowanie...' : 'Eksport PDF'}
            </Button>

            <Button asChild>
              <Link href={`/patients/${data.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                Edytuj
              </Link>
            </Button>

            {data.isArchived ? (
              <Button variant="outline" onClick={handleUnarchive} disabled={isMutating}>
                <RotateCcw className="mr-2 h-4 w-4" />
                {unarchiveMutation.isPending ? 'Przywracanie...' : 'Odarchiwizuj'}
              </Button>
            ) : (
              <Button variant="destructive" onClick={handleArchive} disabled={isMutating}>
                <Archive className="mr-2 h-4 w-4" />
                Archiwizuj
              </Button>
            )}
          </div>
        )}
      </div>

      {isLoading && (
        <Card>
          <CardContent className="p-8 text-center text-muted-foreground">
            Ładowanie danych pacjenta...
          </CardContent>
        </Card>
      )}

      {isError && (
        <Card>
          <CardContent className="p-8 text-center text-destructive">
            Nie udało się pobrać pacjenta
            {error instanceof Error && <p className="mt-2 text-sm">{error.message}</p>}
          </CardContent>
        </Card>
      )}

      {data && (
        <>
          <div className="space-y-6">
            <PatientOverview patient={data} />

            <div className="grid gap-4 lg:grid-cols-3">
              <InfoPanel
                title="Kontakt"
                description="Podstawowe dane do komunikacji"
                icon={<Phone className="h-5 w-5" />}
              >
                <InfoRow icon={<Mail className="h-4 w-4" />} label="E-mail" value={data.email} />
                <InfoRow icon={<Phone className="h-4 w-4" />} label="Telefon" value={data.phoneNumber} />
                <InfoRow icon={<MapPin className="h-4 w-4" />} label="Adres" value={data.address} multiline />
              </InfoPanel>

              <InfoPanel
                title="Dane pacjenta"
                description="Identyfikacja i podstawowe informacje"
                icon={<IdCard className="h-5 w-5" />}
              >
                <InfoRow icon={<IdCard className="h-4 w-4" />} label="PESEL" value={data.pesel} />
                <InfoRow icon={<Cake className="h-4 w-4" />} label="Data urodzenia" value={formatNullableDate(data.dateOfBirth)} />
                <InfoRow icon={<Clock className="h-4 w-4" />} label="Wiek" value={getAgeLabel(data.dateOfBirth)} />
              </InfoPanel>

              <InfoPanel
                title="Status dokumentacji"
                description="Informacje techniczne rekordu"
                icon={<ShieldCheck className="h-5 w-5" />}
              >
                <InfoRow icon={<Calendar className="h-4 w-4" />} label="Dodano" value={formatDateTime(data.createdAt)} />
                <InfoRow icon={<Clock className="h-4 w-4" />} label="Aktualizacja" value={data.updatedAt ? formatDateTime(data.updatedAt) : null} />
                <InfoRow
                  icon={data.isArchived ? <Archive className="h-4 w-4" /> : <ShieldCheck className="h-4 w-4" />}
                  label="Status"
                  value={data.isArchived ? 'Archiwalny' : 'Aktywny'}
                />
              </InfoPanel>
            </div>

            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-lg">
                  <HeartPulse className="h-5 w-5 text-primary" />
                  Informacje medyczne
                </CardTitle>
                <CardDescription>
                  Najważniejsze informacje terapeutyczne zapisane w karcie pacjenta.
                </CardDescription>
              </CardHeader>

              <CardContent className="grid gap-4 md:grid-cols-3">
                <MedicalNote
                  icon={<NotebookText className="h-5 w-5" />}
                  label="Historia medyczna"
                  value={data.medicalHistory}
                />

                <MedicalNote
                  icon={<Pill className="h-5 w-5" />}
                  label="Aktualne leki"
                  value={data.medications}
                />

                <MedicalNote
                  icon={<FileText className="h-5 w-5" />}
                  label="Notatki ogólne"
                  value={data.generalNotes}
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Moduły pacjenta</CardTitle>
                <CardDescription>Wybierz moduł, którym chcesz zarządzać</CardDescription>
              </CardHeader>

              <CardContent className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                <Link
                  href={`/patients/${data.id}/body-maps`}
                  className="rounded-lg border p-4 transition-colors hover:bg-accent/30"
                >
                  <div className="flex items-center gap-3">
                    <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-lg bg-red-50 dark:bg-red-950">
                      <Activity className="h-5 w-5 text-red-500" />
                    </div>
                    <div>
                      <div className="font-medium">Mapy bólu</div>
                      <p className="text-xs text-muted-foreground">Interaktywne mapy z punktami bólu</p>
                    </div>
                  </div>
                </Link>

                <ModulePlaceholder
                  icon={<Calendar className="h-5 w-5 text-blue-500" />}
                  bg="bg-blue-50 dark:bg-blue-950"
                  label="Wizyty"
                  description="Historia i nadchodzące spotkania"
                />

                <ModulePlaceholder
                  icon={<FileText className="h-5 w-5 text-green-500" />}
                  bg="bg-green-50 dark:bg-green-950"
                  label="EDM"
                  description="Dokumentacja medyczna i załączniki"
                />

                <ModulePlaceholder
                  icon={<Dumbbell className="h-5 w-5 text-purple-500" />}
                  bg="bg-purple-50 dark:bg-purple-950"
                  label="Plany terapii"
                  description="Ćwiczenia, serie, powtórzenia"
                />

                <ModulePlaceholder
                  icon={<ClipboardList className="h-5 w-5 text-orange-500" />}
                  bg="bg-orange-50 dark:bg-orange-950"
                  label="Notatki"
                  description="Dodatkowe obserwacje terapeuty"
                />
              </CardContent>
            </Card>
          </div>

          <Dialog open={archiveDialogOpen} onOpenChange={setArchiveDialogOpen}>
            <DialogContent>
              <DialogHeader>
                <div className="mb-2 flex h-11 w-11 items-center justify-center rounded-full bg-destructive/10 text-destructive">
                  <AlertTriangle className="h-5 w-5" />
                </div>

                <DialogTitle>Archiwizować pacjenta?</DialogTitle>

                <DialogDescription>
                  Pacjent <span className="font-medium text-foreground">{data.firstName} {data.lastName}</span> zostanie
                  przeniesiony do archiwum. Nie zniknie z bazy danych i będzie można go później przywrócić.
                </DialogDescription>
              </DialogHeader>

              <div className="rounded-lg border bg-muted/40 p-4 text-sm text-muted-foreground">
                Po archiwizacji pacjent nie będzie widoczny na liście aktywnych pacjentów. Znajdziesz go w filtrze
                <span className="font-medium text-foreground"> Archiwalni</span>.
              </div>

              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setArchiveDialogOpen(false)}
                  disabled={archiveMutation.isPending}
                >
                  Anuluj
                </Button>

                <Button
                  type="button"
                  variant="destructive"
                  onClick={handleConfirmArchive}
                  disabled={archiveMutation.isPending}
                >
                  <Archive className="mr-2 h-4 w-4" />
                  {archiveMutation.isPending ? 'Archiwizowanie...' : 'Archiwizuj pacjenta'}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </>
      )}
    </div>
  );
}

function PatientOverview({ patient }: { patient: PatientDetail }) {
  return (
    <Card className="overflow-hidden">
      <div className="border-b bg-gradient-to-br from-primary/10 via-background to-background p-6">
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
            <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-primary text-2xl font-semibold text-primary-foreground shadow-sm">
              {getInitials(patient)}
            </div>

            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="text-3xl font-semibold tracking-tight">
                  {patient.firstName} {patient.lastName}
                </h1>

                <span
                  className={cn(
                    'inline-flex items-center rounded-full px-3 py-1 text-xs font-medium',
                    patient.isArchived
                      ? 'bg-muted text-muted-foreground'
                      : 'bg-emerald-50 text-emerald-700'
                  )}
                >
                  {patient.isArchived ? 'Archiwalny' : 'Aktywny'}
                </span>
              </div>

              <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                Karta pacjenta z danymi kontaktowymi, informacjami medycznymi i dostępem do modułów terapii.
              </p>
            </div>
          </div>

          <div className="grid gap-2 sm:grid-cols-2 lg:min-w-80">
            <QuickStat label="PESEL" value={patient.pesel ?? 'Brak'} />
            <QuickStat label="Wiek" value={getAgeLabel(patient.dateOfBirth) ?? 'Brak'} />
          </div>
        </div>
      </div>

      <CardContent className="grid gap-3 p-4 md:grid-cols-3">
        <ContactChip icon={<Mail className="h-4 w-4" />} value={patient.email ?? 'Brak e-maila'} />
        <ContactChip icon={<Phone className="h-4 w-4" />} value={patient.phoneNumber ?? 'Brak telefonu'} />
        <ContactChip icon={<MapPin className="h-4 w-4" />} value={patient.address ?? 'Brak adresu'} />
      </CardContent>
    </Card>
  );
}

function InfoPanel({
  title,
  description,
  icon,
  children,
}: {
  title: string;
  description: string;
  icon: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card>
      <CardHeader className="space-y-2">
        <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
          {icon}
        </div>
        <div>
          <CardTitle className="text-lg">{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">{children}</CardContent>
    </Card>
  );
}

function InfoRow({
  icon,
  label,
  value,
  multiline = false,
}: {
  icon: ReactNode;
  label: string;
  value: string | null | undefined;
  multiline?: boolean;
}) {
  return (
    <div className="flex gap-3">
      <div className="mt-0.5 text-muted-foreground">{icon}</div>
      <div className="min-w-0">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
        {value ? (
          <p className={cn('mt-0.5 text-sm text-foreground', multiline && 'whitespace-pre-line')}>
            {value}
          </p>
        ) : (
          <p className="mt-0.5 text-sm italic text-muted-foreground">brak</p>
        )}
      </div>
    </div>
  );
}

function MedicalNote({
  icon,
  label,
  value,
}: {
  icon: ReactNode;
  label: string;
  value: string | null;
}) {
  return (
    <div className="rounded-lg border bg-muted/30 p-4">
      <div className="mb-3 flex items-center gap-2 text-primary">
        {icon}
        <p className="font-medium text-foreground">{label}</p>
      </div>

      {value ? (
        <p className="whitespace-pre-line text-sm text-muted-foreground">{value}</p>
      ) : (
        <p className="text-sm italic text-muted-foreground">Brak informacji</p>
      )}
    </div>
  );
}

function ContactChip({ icon, value }: { icon: ReactNode; value: string }) {
  return (
    <div className="flex min-w-0 items-center gap-2 rounded-lg border bg-background px-3 py-2 text-sm text-muted-foreground">
      <span className="shrink-0">{icon}</span>
      <span className="truncate">{value}</span>
    </div>
  );
}

function QuickStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-background/80 px-4 py-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="mt-1 truncate text-sm font-semibold">{value}</p>
    </div>
  );
}

function ModulePlaceholder({
  icon,
  bg,
  label,
  description,
}: {
  icon: ReactNode;
  bg: string;
  label: string;
  description: string;
}) {
  return (
    <div className="cursor-not-allowed rounded-lg border p-4 opacity-50">
      <div className="flex items-center gap-3">
        <div className={`flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-lg ${bg}`}>
          {icon}
        </div>
        <div>
          <div className="font-medium">
            {label} <span className="text-xs text-muted-foreground">(wkrótce)</span>
          </div>
          <p className="text-xs text-muted-foreground">{description}</p>
        </div>
      </div>
    </div>
  );
}

function getInitials(patient: PatientDetail) {
  const firstInitial = patient.firstName?.trim().charAt(0) ?? '';
  const lastInitial = patient.lastName?.trim().charAt(0) ?? '';
  const initials = `${firstInitial}${lastInitial}`.toUpperCase();

  return initials || 'P';
}

function formatNullableDate(value: string | null) {
  if (!value) return null;
  return formatDate(value);
}

function getAgeLabel(dateOfBirth: string | null) {
  if (!dateOfBirth) return null;

  const birthDate = new Date(dateOfBirth);
  const today = new Date();

  if (Number.isNaN(birthDate.getTime())) return null;

  let age = today.getFullYear() - birthDate.getFullYear();
  const monthDifference = today.getMonth() - birthDate.getMonth();
  const dayDifference = today.getDate() - birthDate.getDate();

  if (monthDifference < 0 || (monthDifference === 0 && dayDifference < 0)) {
    age -= 1;
  }

  if (age < 0) return null;

  if (age === 1) return '1 rok';
  if (age >= 2 && age <= 4) return `${age} lata`;
  return `${age} lat`;
}
