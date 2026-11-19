'use client';

import {
  useState,
  type ReactNode,
} from 'react';
import Link from 'next/link';
import {
  useParams,
  useRouter,
} from 'next/navigation';
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  Activity,
  Archive,
  ArrowLeft,
  Calendar,
  Cake,
  ClipboardList,
  Clock,
  Dumbbell,
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
import type {
  LucideIcon,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import PatientExportDialog from '@/components/patients/PatientExportDialog';
import { PatientPainAnalytics } from '@/components/analytics/AnalyticsPanels';
import { useAuthStore } from '@/stores/auth-store';
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
import {
  formatDate,
  formatDateTime,
} from '@/lib/utils';

export default function PatientDetailPage() {
  const { id } = useParams<{
    id: string;
  }>();

  const router = useRouter();
  const accountId = useAuthStore((state) => state.isAuthenticated && state.roles.includes('Therapist') ? state.userId : null);
  const client = useQueryClient();
  const [confirmArchive, setConfirmArchive] =
    useState(false);

  const query = useQuery({
    queryKey: ['patient', id, accountId],
    queryFn: () =>
      patientsApi.getById(id),
    enabled: Boolean(id && accountId),
  });

  const patient = query.data;

  const archive = useMutation({
    mutationFn: (restore: boolean) =>
      restore
        ? patientsApi.unarchive(id)
        : patientsApi.archive(id),
    onSuccess: async (_data, restore) => {
      setConfirmArchive(false);

      toast.success(
        restore
          ? 'Pacjent przywrócony z archiwum'
          : 'Pacjent zarchiwizowany',
      );

      await Promise.all([
        client.invalidateQueries({
          queryKey: ['patients'],
        }),
        client.invalidateQueries({
          queryKey: ['patient', id],
        }),
        client.invalidateQueries({
          queryKey: ['therapy-patient'],
        }),
      ]);

      if (!restore) {
        router.push('/patients');
      }
    },
    onError: () =>
      toast.error(
        'Nie udało się zmienić statusu pacjenta.',
      ),
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Button
          asChild
          variant="ghost"
          size="sm"
        >
          <Link href="/patients">
            <ArrowLeft
              aria-hidden="true"
              className="mr-2 h-4 w-4"
            />
            Wróć do listy
          </Link>
        </Button>

        {patient && (
          <div className="flex flex-wrap gap-2">
            <PatientExportDialog patientId={id} />

            <Button asChild>
              <Link
                href={`/patients/${id}/edit`}
              >
                <Pencil
                  aria-hidden="true"
                  className="mr-2 h-4 w-4"
                />
                Edytuj
              </Link>
            </Button>

            {patient.isArchived ? (
              <Button
                type="button"
                variant="outline"
                disabled={
                  archive.isPending
                }
                onClick={() =>
                  archive.mutate(true)
                }
              >
                <RotateCcw
                  aria-hidden="true"
                  className="mr-2 h-4 w-4"
                />
                {archive.isPending
                  ? 'Przywracanie…'
                  : 'Odarchiwizuj'}
              </Button>
            ) : (
              <Button
                type="button"
                variant="destructive"
                disabled={
                  archive.isPending
                }
                onClick={() =>
                  setConfirmArchive(true)
                }
              >
                <Archive
                  aria-hidden="true"
                  className="mr-2 h-4 w-4"
                />
                Archiwizuj
              </Button>
            )}
          </div>
        )}
      </div>

      {query.isPending && (
        <p role="status">
          Ładowanie danych pacjenta…
        </p>
      )}

      {query.isError && (
        <div className="space-y-3 rounded-xl border p-5">
          <p
            role="alert"
            className="text-destructive"
          >
            Nie udało się pobrać pacjenta.
          </p>
          <Button
            type="button"
            variant="outline"
            onClick={() =>
              void query.refetch()
            }
          >
            Spróbuj ponownie
          </Button>
        </div>
      )}

      {patient && !query.isError && (
        <>
          <Card className="overflow-hidden">
            <div className="border-b bg-gradient-to-br from-primary/10 via-background to-background p-6">
              <div className="flex flex-col justify-between gap-6 lg:flex-row">
                <div className="flex items-center gap-4">
                  <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-primary text-2xl font-semibold text-primary-foreground">
                    {`${patient.firstName.charAt(0)}${patient.lastName.charAt(0)}`.toUpperCase()}
                  </div>

                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <h1 className="text-3xl">
                        {patient.firstName}{' '}
                        {patient.lastName}
                      </h1>

                      <span
                        className={`rounded-full px-3 py-1 text-xs ${
                          patient.isArchived
                            ? 'bg-muted text-muted-foreground'
                            : 'bg-emerald-50 text-emerald-700'
                        }`}
                      >
                        {patient.isArchived
                          ? 'Archiwalny'
                          : 'Aktywny'}
                      </span>

                      {patient.isLinkedToAccount && (
                        <span className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
                          Konto pacjenta
                        </span>
                      )}
                    </div>

                    <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                      Karta pacjenta z danymi
                      kontaktowymi, informacjami
                      medycznymi i dostępem do
                      modułów terapii.
                    </p>
                  </div>
                </div>

                <div className="grid gap-2 sm:grid-cols-2 lg:min-w-80">
                  {[
                    [
                      'PESEL',
                      patient.pesel ||
                        'Brak',
                    ],
                    [
                      'Wiek',
                      ageLabel(
                        patient.dateOfBirth,
                      ) || 'Brak',
                    ],
                  ].map(([label, value]) => (
                    <div
                      key={label}
                      className="rounded-lg border bg-background/80 px-4 py-3"
                    >
                      <p className="text-xs uppercase text-muted-foreground">
                        {label}
                      </p>
                      <p className="mt-1 text-sm font-semibold">
                        {value}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <CardContent className="grid gap-3 p-4 md:grid-cols-3">
              {[
                {
                  icon: Mail,
                  value:
                    patient.email ||
                    'Brak e-maila',
                },
                {
                  icon: Phone,
                  value:
                    patient.phoneNumber ||
                    'Brak telefonu',
                },
                {
                  icon: MapPin,
                  value:
                    patient.address ||
                    'Brak adresu',
                },
              ].map(
                (
                  {
                    icon: Icon,
                    value,
                  },
                  index,
                ) => (
                  <div
                    key={index}
                    className="flex min-w-0 items-center gap-2 rounded-lg border px-3 py-2 text-sm text-muted-foreground"
                  >
                    <Icon
                      aria-hidden="true"
                      className="h-4 w-4 shrink-0"
                    />
                    <span className="truncate">
                      {value}
                    </span>
                  </div>
                ),
              )}
            </CardContent>
          </Card>

          <div className="grid gap-4 lg:grid-cols-3">
            <InfoPanel
              title="Kontakt"
              description="Podstawowe dane do komunikacji"
              icon={Phone}
            >
              <InfoRow
                icon={Mail}
                label="E-mail"
                value={patient.email}
              />
              <InfoRow
                icon={Phone}
                label="Telefon"
                value={
                  patient.phoneNumber
                }
              />
              <InfoRow
                icon={MapPin}
                label="Adres"
                value={patient.address}
              />
            </InfoPanel>

            <InfoPanel
              title="Dane pacjenta"
              description="Identyfikacja i podstawowe informacje"
              icon={IdCard}
            >
              <InfoRow
                icon={IdCard}
                label="PESEL"
                value={patient.pesel}
              />
              <InfoRow
                icon={Cake}
                label="Data urodzenia"
                value={
                  patient.dateOfBirth
                    ? formatDate(
                        patient.dateOfBirth,
                      )
                    : null
                }
              />
              <InfoRow
                icon={Clock}
                label="Wiek"
                value={ageLabel(
                  patient.dateOfBirth,
                )}
              />
            </InfoPanel>

            <InfoPanel
              title="Status dokumentacji"
              description="Informacje techniczne rekordu"
              icon={ShieldCheck}
            >
              <InfoRow
                icon={Calendar}
                label="Dodano"
                value={formatDateTime(
                  patient.createdAt,
                )}
              />
              <InfoRow
                icon={Clock}
                label="Aktualizacja"
                value={
                  patient.updatedAt
                    ? formatDateTime(
                        patient.updatedAt,
                      )
                    : null
                }
              />
              <InfoRow
                icon={
                  patient.isArchived
                    ? Archive
                    : ShieldCheck
                }
                label="Status"
                value={
                  patient.isArchived
                    ? 'Archiwalny'
                    : 'Aktywny'
                }
              />
            </InfoPanel>
          </div>

          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-lg">
                <HeartPulse
                  aria-hidden="true"
                  className="h-5 w-5 text-primary"
                />
                Informacje medyczne
              </CardTitle>
              <CardDescription>
                Najważniejsze informacje
                terapeutyczne zapisane
                w karcie pacjenta.
              </CardDescription>
            </CardHeader>

            <CardContent className="grid gap-4 md:grid-cols-3">
              {[
                {
                  label:
                    'Historia medyczna',
                  icon: NotebookText,
                  value:
                    patient.medicalHistory,
                },
                {
                  label:
                    'Aktualne leki',
                  icon: Pill,
                  value:
                    patient.medications,
                },
                {
                  label:
                    'Notatki ogólne',
                  icon: FileText,
                  value:
                    patient.generalNotes,
                },
              ].map(
                ({
                  label,
                  icon: Icon,
                  value,
                }) => (
                  <div
                    key={label}
                    className="rounded-lg border bg-muted/30 p-4"
                  >
                    <div className="mb-3 flex items-center gap-2">
                      <Icon
                        aria-hidden="true"
                        className="h-5 w-5 text-primary"
                      />
                      <p className="font-medium">
                        {label}
                      </p>
                    </div>

                    <p
                      className={`whitespace-pre-wrap break-words text-sm text-muted-foreground ${
                        value ? '' : 'italic'
                      }`}
                    >
                      {value ||
                        'Brak informacji'}
                    </p>
                  </div>
                ),
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">
                Moduły pacjenta
              </CardTitle>
              <CardDescription>
                Wybierz moduł, którym chcesz
                zarządzać
              </CardDescription>
            </CardHeader>

            <CardContent className="grid gap-3 md:grid-cols-2 lg:grid-cols-3">
              <ModuleLink
                href={`/patients/${id}/body-maps`}
                label="Mapy bólu"
                description="Interaktywne mapy z punktami bólu"
                icon={Activity}
              />
              <ModuleLink
                href={`/patients/${id}/documentation`}
                label="Dokumentacja terapii"
                description="Wpisy z wizyt i historia korekt"
                icon={FileText}
              />
              <ModuleLink
                href={`/patients/${id}/therapy-plans`}
                label="Plany terapii"
                description="Ćwiczenia, parametry i historia wersji"
                icon={Dumbbell}
              />
              <ModuleLink
                href={`/patients/${id}/exercise-journal`}
                label="Dziennik ćwiczeń"
                description="Zgłoszenia pacjenta i podsumowanie realizacji"
                icon={NotebookText}
              />
              <ModuleLink
                href={`/messages?patient=${encodeURIComponent(id)}`}
                label="Wiadomości"
                description="Rozmowy w ramach współpracy z pacjentem"
                icon={Mail}
              />
              <Placeholder
                label="Wizyty"
                description="Historia i nadchodzące spotkania"
                icon={Calendar}
              />
              <Placeholder
                label="Notatki"
                description="Dodatkowe obserwacje terapeuty"
                icon={ClipboardList}
              />
            </CardContent>
          </Card>

          {accountId && !patient.isArchived && patient.isLinkedToAccount && (
            <PatientPainAnalytics key={`${accountId}:${id}`} patientId={id} />
          )}

          <Dialog
            open={confirmArchive}
            onOpenChange={(open) => {
              if (!archive.isPending) {
                setConfirmArchive(open);
              }
            }}
          >
            <DialogContent>
              <DialogHeader>
                <DialogTitle>
                  Archiwizować pacjenta?
                </DialogTitle>
                <DialogDescription>
                  Pacjent{' '}
                  {patient.firstName}{' '}
                  {patient.lastName} zostanie
                  przeniesiony do archiwum.
                  Dane nie zostaną usunięte.
                  Kartę można później
                  przywrócić.
                </DialogDescription>
              </DialogHeader>

              <p className="text-sm text-muted-foreground">
                Pacjenta znajdziesz
                w filtrze „Archiwalni”.
                Plany pozostaną do odczytu,
                a ich zmiany zostaną
                zablokowane.
              </p>

              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  disabled={
                    archive.isPending
                  }
                  onClick={() =>
                    setConfirmArchive(false)
                  }
                >
                  Anuluj
                </Button>
                <Button
                  type="button"
                  variant="destructive"
                  disabled={
                    archive.isPending
                  }
                  onClick={() =>
                    archive.mutate(false)
                  }
                >
                  {archive.isPending
                    ? 'Archiwizowanie…'
                    : 'Archiwizuj pacjenta'}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </>
      )}
    </div>
  );
}

function InfoPanel({
  title,
  description,
  icon: Icon,
  children,
}: {
  title: string;
  description: string;
  icon: LucideIcon;
  children: ReactNode;
}) {
  return (
    <Card>
      <CardHeader>
        <span className="mb-2 flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Icon
            aria-hidden="true"
            className="h-5 w-5"
          />
        </span>
        <CardTitle className="text-lg">
          {title}
        </CardTitle>
        <CardDescription>
          {description}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {children}
      </CardContent>
    </Card>
  );
}

function InfoRow({
  icon: Icon,
  label,
  value,
}: {
  icon: LucideIcon;
  label: string;
  value: string | null;
}) {
  return (
    <div className="flex gap-3">
      <Icon
        aria-hidden="true"
        className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"
      />
      <div className="min-w-0">
        <p className="text-xs uppercase tracking-wide text-muted-foreground">
          {label}
        </p>
        <p
          className={`mt-1 whitespace-pre-wrap break-words text-sm ${
            value
              ? ''
              : 'italic text-muted-foreground'
          }`}
        >
          {value || 'brak'}
        </p>
      </div>
    </div>
  );
}

function ModuleLink({
  href,
  label,
  description,
  icon: Icon,
}: {
  href: string;
  label: string;
  description: string;
  icon: LucideIcon;
}) {
  return (
    <Link
      href={href}
      prefetch={false}
      className="rounded-lg border p-4 transition-colors hover:bg-accent/30"
    >
      <div className="flex items-center gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10">
          <Icon
            aria-hidden="true"
            className="h-5 w-5 text-primary"
          />
        </span>
        <div>
          <p className="font-medium">
            {label}
          </p>
          <p className="text-xs text-muted-foreground">
            {description}
          </p>
        </div>
      </div>
    </Link>
  );
}

function Placeholder({
  label,
  description,
  icon: Icon,
}: {
  label: string;
  description: string;
  icon: LucideIcon;
}) {
  return (
    <div className="rounded-lg border p-4 opacity-50">
      <div className="flex items-center gap-3">
        <Icon
          aria-hidden="true"
          className="h-5 w-5 shrink-0"
        />
        <div>
          <p className="font-medium">
            {label}{' '}
            <span className="text-xs">
              (wkrótce)
            </span>
          </p>
          <p className="text-xs text-muted-foreground">
            {description}
          </p>
        </div>
      </div>
    </div>
  );
}

function ageLabel(value: string | null) {
  if (!value) return null;

  const birth = new Date(value);
  const today = new Date();

  if (Number.isNaN(birth.getTime())) {
    return null;
  }

  let age =
    today.getFullYear() -
    birth.getFullYear();

  if (
    today.getMonth() <
      birth.getMonth() ||
    (today.getMonth() ===
      birth.getMonth() &&
      today.getDate() <
        birth.getDate())
  ) {
    age--;
  }

  if (age < 0) return null;

  return age === 1
    ? '1 rok'
    : age >= 2 && age <= 4
      ? `${age} lata`
      : `${age} lat`;
}
