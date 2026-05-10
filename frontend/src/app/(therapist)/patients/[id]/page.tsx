'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AxiosError } from 'axios';
import { Archive, ArrowLeft, Mail, MapPin, Pencil, Phone, User } from 'lucide-react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import { formatDate, formatDateTime } from '@/lib/utils';

export default function PatientDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const id = params?.id;
  const queryClient = useQueryClient();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['patient', id],
    queryFn: () => patientsApi.getById(id!),
    enabled: !!id,
  });

  const archiveMutation = useMutation({
    mutationFn: () => patientsApi.archive(id!),
    onSuccess: async () => {
      toast.success('Pacjent zarchiwizowany');
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

  const isMutating = archiveMutation.isPending || unarchiveMutation.isPending;

  const handleArchive = () => {
    if (!id || isMutating || !data || data.isArchived) return;
    const confirmed = window.confirm(
      `Czy na pewno chcesz zarchiwizować pacjenta ${data.firstName} ${data.lastName}?`,
    );
    if (!confirmed) return;
    archiveMutation.mutate();
  };

  const handleUnarchive = () => {
    if (!id || isMutating || !data || !data.isArchived) return;
    unarchiveMutation.mutate();
  };

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" asChild>
          <Link href="/patients">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Wróć do listy
          </Link>
        </Button>

        {data && (
          <div className="flex items-center gap-2">
            <Button asChild>
              <Link href={`/patients/${data.id}/edit`}>
                <Pencil className="mr-2 h-4 w-4" />
                Edytuj
              </Link>
            </Button>

            {data.isArchived ? (
              <Button
                variant="outline"
                onClick={handleUnarchive}
                disabled={isMutating}
              >
                {unarchiveMutation.isPending ? 'Przywracanie...' : 'Odarchiwizuj'}
              </Button>
            ) : (
              <Button
                variant="destructive"
                onClick={handleArchive}
                disabled={isMutating}
              >
                <Archive className="mr-2 h-4 w-4" />
                {archiveMutation.isPending ? 'Archiwizowanie...' : 'Archiwizuj'}
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
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <div className="flex items-start gap-4">
                <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
                  <User className="h-7 w-7" />
                </div>
                <div className="flex-1">
                  <CardTitle className="text-3xl">
                    {data.firstName} {data.lastName}
                  </CardTitle>
                  <CardDescription className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1">
                    {data.pesel && <span>PESEL: {data.pesel}</span>}
                    {data.dateOfBirth && (
                      <span>Ur. {formatDate(data.dateOfBirth)}</span>
                    )}
                    {data.isArchived && (
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs">
                        Zarchiwizowany
                      </span>
                    )}
                  </CardDescription>
                </div>
              </div>
            </CardHeader>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Dane kontaktowe</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="flex items-center gap-3">
                <Mail className="h-4 w-4 text-muted-foreground" />
                <span>{data.email ?? <em className="text-muted-foreground">brak</em>}</span>
              </div>
              <div className="flex items-center gap-3">
                <Phone className="h-4 w-4 text-muted-foreground" />
                <span>{data.phoneNumber ?? <em className="text-muted-foreground">brak</em>}</span>
              </div>
              <div className="flex items-start gap-3">
                <MapPin className="mt-0.5 h-4 w-4 text-muted-foreground" />
                <span className="whitespace-pre-line">
                  {data.address ?? <em className="text-muted-foreground">brak</em>}
                </span>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Informacje medyczne</CardTitle>
            </CardHeader>
            <CardContent className="space-y-5 text-sm">
              <DetailField label="Historia medyczna" value={data.medicalHistory} />
              <DetailField label="Aktualne leki" value={data.medications} />
              <DetailField label="Notatki ogólne" value={data.generalNotes} />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="flex flex-wrap justify-between gap-2 p-4 text-xs text-muted-foreground">
              <span>Dodano: {formatDateTime(data.createdAt)}</span>
              {data.updatedAt && <span>Aktualizacja: {formatDateTime(data.updatedAt)}</span>}
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  );
}

function DetailField({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <p className="mb-1 font-medium text-foreground">{label}</p>
      {value ? (
        <p className="whitespace-pre-line text-muted-foreground">{value}</p>
      ) : (
        <p className="italic text-muted-foreground">brak</p>
      )}
    </div>
  );
}
