'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';

import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import { RecordFailure } from '@/components/medical-records/MedicalRecordUI';
import { appointmentsApi } from '@/lib/appointments-api';
import { patientsApi } from '@/lib/api';
import {
  recordsApi,
  recordOptions,
  recordDate,
  isRecordId,
} from '@/lib/medical-records-api';
import { useAuthStore } from '@/stores/auth-store';

export default function MedicalRecordList({
  patientId,
  appointmentId,
}: {
  patientId: string;
  appointmentId: string;
}) {
  const accountId = useAuthStore(
    (state) => state.userId,
  );
  const [page, setPage] = useState(1);
  const valid = isRecordId(patientId);

  const patient = useQuery({
    queryKey: [
      'documentation-patient',
      accountId,
      patientId,
    ],
    queryFn: () => patientsApi.getById(patientId),
    enabled: valid && Boolean(accountId),
    ...recordOptions,
  });

  const list = useQuery({
    queryKey: [
      'medical-records',
      accountId,
      patientId,
      page,
    ],
    queryFn: ({ signal }) =>
      recordsApi.list(patientId, page, signal),
    enabled:
      valid &&
      Boolean(patient.data) &&
      !patient.isError,
    ...recordOptions,
  });

  if (!valid) {
    return (
      <p role="alert">
        Nieprawidłowy identyfikator pacjenta.
      </p>
    );
  }

  if (patient.isError) {
    return (
      <RecordFailure
        error={patient.error}
        retry={() => void patient.refetch()}
      />
    );
  }

  if (patient.isPending) {
    return (
      <LoadingState title="Wczytywanie karty pacjenta…" />
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      <Button asChild variant="outline">
        <Link href={`/patients/${patientId}`}>
          Wróć do karty pacjenta
        </Link>
      </Button>

      <header className="space-y-3">
        <h1 className="text-4xl">
          Dokumentacja terapii
        </h1>

        <p>
          {patient.data.firstName}{' '}
          {patient.data.lastName}
        </p>

        <p className="text-sm text-muted-foreground">
          Szkice, zatwierdzone wpisy i historia korekt.
          Treści nie są na tym etapie udostępniane
          w portalu pacjenta.
        </p>
      </header>

      {appointmentId ? (
        <AppointmentRecord
          key={`${accountId}:${appointmentId}`}
          accountId={accountId ?? ''}
          patientId={patientId}
          appointmentId={appointmentId}
        />
      ) : (
        <section className="space-y-3 rounded-xl border bg-card p-5">
          <h2 className="text-2xl">
            Nowa dokumentacja wizyty
          </h2>

          <p className="text-sm text-muted-foreground">
            Otwórz wizytę w kalendarzu i wybierz
            „Dokumentacja wizyty”. Jeden termin
            ma jeden wpis.
          </p>

          <Button asChild>
            <Link href="/calendar">
              Przejdź do kalendarza
            </Link>
          </Button>
        </section>
      )}

      <div className="flex justify-between gap-3">
        <h2 className="text-3xl">
          Historia wpisów
        </h2>

        <Button
          type="button"
          variant="outline"
          disabled={list.isFetching}
          onClick={() => void list.refetch()}
        >
          Odśwież
        </Button>
      </div>

      {list.isError ? (
        <RecordFailure
          error={list.error}
          retry={() => void list.refetch()}
        />
      ) : list.isPending ? (
        <LoadingState title="Pobieranie dokumentacji…" />
      ) : (
        <>
          {!list.data.items.length && (
            <p className="rounded-xl border border-dashed p-6">
              {page > 1
                ? 'Ta strona jest pusta.'
                : 'Nie dodano jeszcze dokumentacji wizyt.'}
            </p>
          )}

          <div className="space-y-3">
            {list.data.items.map((item) => (
              <article
                key={item.id}
                className="flex flex-col justify-between gap-4 rounded-xl border bg-card p-5 sm:flex-row sm:items-center"
              >
                <div className="space-y-2">
                  <h3 className="text-2xl">
                    {item.appointmentStartsAt
                      ? `Wizyta: ${recordDate(
                          item.appointmentStartsAt,
                        )}`
                      : 'Starszy wpis bez powiązanej wizyty'}
                  </h3>

                  <p className="text-sm text-muted-foreground">
                    {item.status === 'Draft'
                      ? 'Szkic'
                      : `Zatwierdzona · wersja ${item.currentVersion}`}
                  </p>
                </div>

                <Button asChild variant="outline">
                  <Link
                    href={`/patients/${patientId}/documentation/${item.id}`}
                  >
                    Otwórz dokumentację
                  </Link>
                </Button>
              </article>
            ))}
          </div>

          {(list.data.totalPages > 1 ||
            page > 1) && (
            <nav
              aria-label="Strony dokumentacji"
              className="flex items-center justify-between gap-3"
            >
              <Button
                type="button"
                variant="outline"
                disabled={
                  page <= 1 || list.isFetching
                }
                onClick={() => setPage(page - 1)}
              >
                Poprzednia
              </Button>

              <span className="text-sm">
                {page} /{' '}
                {Math.max(1, list.data.totalPages)}
              </span>

              <Button
                type="button"
                variant="outline"
                disabled={
                  !list.data.hasNext ||
                  list.isFetching
                }
                onClick={() => setPage(page + 1)}
              >
                Następna
              </Button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}

function AppointmentRecord({
  accountId,
  patientId,
  appointmentId,
}: {
  accountId: string;
  patientId: string;
  appointmentId: string;
}) {
  const router = useRouter();
  const client = useQueryClient();
  const lock = useRef(false);
  const valid = isRecordId(appointmentId);

  const query = useQuery({
    queryKey: [
      'appointment-documentation',
      accountId,
      patientId,
      appointmentId,
    ],
    queryFn: async ({ signal }) => {
      const appointment =
        await appointmentsApi.details(
          appointmentId,
          signal,
        );

      if (appointment.patientId !== patientId) {
        throw new Error('invalid-patient');
      }

      const record =
        await recordsApi.findForAppointment(
          patientId,
          appointmentId,
          signal,
        );

      return { appointment, record };
    },
    enabled: valid,
    ...recordOptions,
  });

  const create = useMutation({
    mutationFn: () =>
      recordsApi.create(
        patientId,
        appointmentId,
      ),
    retry: false,
    onSuccess: async (record) => {
      await client.invalidateQueries({
        queryKey: [
          'medical-records',
          accountId,
          patientId,
        ],
      });

      router.push(
        `/patients/${patientId}/documentation/${record.id}`,
      );
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  if (!valid) {
    return (
      <p role="alert">
        Nieprawidłowy identyfikator wizyty.
      </p>
    );
  }

  if (query.isError) {
    return (
      <RecordFailure
        error={query.error}
        retry={() => void query.refetch()}
      />
    );
  }

  if (query.isPending) {
    return (
      <LoadingState title="Sprawdzanie dokumentacji wizyty…" />
    );
  }

  return (
    <section className="space-y-4 rounded-xl border border-primary/30 bg-primary/5 p-5">
      <h2 className="text-2xl">
        Wizyta:{' '}
        {recordDate(
          query.data.appointment.startsAt,
        )}
      </h2>

      {query.data.record ? (
        <Button asChild>
          <Link
            href={`/patients/${patientId}/documentation/${query.data.record.id}`}
          >
            Otwórz istniejący wpis
          </Link>
        </Button>
      ) : (
        <>
          <p className="text-sm">
            Utwórz pusty szkic, a następnie uzupełnij
            formularz. Zatwierdzenie będzie możliwe
            po oznaczeniu wizyty jako odbytej.
          </p>

          <Button
            type="button"
            disabled={
              create.isPending || query.isFetching
            }
            onClick={() => {
              if (lock.current) return;
              lock.current = true;
              create.mutate();
            }}
          >
            {create.isPending
              ? 'Tworzenie…'
              : 'Utwórz szkic dokumentacji'}
          </Button>
        </>
      )}

      {create.isError && (
        <RecordFailure
          error={create.error}
          retry={() => void query.refetch()}
        />
      )}
    </section>
  );
}