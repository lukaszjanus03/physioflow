'use client';

import {
  useMutation,
  useQuery,
} from '@tanstack/react-query';
import { useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Plus } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import {
  Pages,
  StatusBadge,
  TherapyError,
} from '@/components/therapy/TherapyUI';
import { patientsApi } from '@/lib/api';
import {
  planLabels,
  therapyApi,
  therapyDate,
  therapyId,
  therapyOptions,
} from '@/lib/therapy-api';
import type {
  PlanStatus,
} from '@/lib/therapy-api';
import { useAuthStore } from '@/stores/auth-store';

export default function TherapyPlanList({
  patientId,
}: {
  patientId: string;
}) {
  const accountId = useAuthStore(
    (state) => state.userId,
  );
  const router = useRouter();
  const lock = useRef(false);

  const [filters, setFilters] = useState({
    status: '',
    page: 1,
  });

  const valid = therapyId(patientId);

  const patient = useQuery({
    queryKey: [
      'therapy-patient',
      accountId,
      patientId,
    ],
    queryFn: () =>
      patientsApi.getById(patientId),
    enabled: valid && Boolean(accountId),
    ...therapyOptions,
  });

  const list = useQuery({
    queryKey: [
      'therapy-plans',
      accountId,
      patientId,
      filters,
    ],
    queryFn: ({ signal }) =>
      therapyApi.plans(
        patientId,
        filters.status,
        filters.page,
        signal,
      ),
    enabled:
      Boolean(patient.data) &&
      !patient.isError,
    ...therapyOptions,
  });

  const create = useMutation({
    mutationFn: () =>
      therapyApi.createPlan(patientId),
    retry: false,
    onSuccess: (plan) =>
      router.push(
        `/patients/${patientId}/therapy-plans/${plan.id}`,
      ),
    onError: () => {
      void patient.refetch();
      void list.refetch();
    },
    onSettled: () => {
      lock.current = false;
    },
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
      <TherapyError
        error={patient.error}
        retry={() => void patient.refetch()}
      />
    );
  }

  if (patient.isPending) {
    return (
      <LoadingState title="Pobieranie karty pacjenta…" />
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      <Button asChild variant="outline">
        <Link href={`/patients/${patientId}`}>
          Wróć do karty pacjenta
        </Link>
      </Button>

      <header className="space-y-4 rounded-2xl border bg-gradient-to-br from-primary/10 via-card to-card p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-4xl">
              Plany terapii
            </h1>
            <p className="mt-2">
              {patient.data.firstName}{' '}
              {patient.data.lastName}
            </p>
          </div>

          <Button
            type="button"
            disabled={
              patient.data.isArchived ||
              create.isPending
            }
            onClick={() => {
              if (lock.current) return;
              lock.current = true;
              create.mutate();
            }}
          >
            <Plus
              aria-hidden="true"
              className="mr-2 h-4 w-4"
            />
            {create.isPending
              ? 'Tworzenie…'
              : 'Utwórz szkic planu'}
          </Button>
        </div>

        <p className="text-sm text-muted-foreground">
          Aktywacja zapisuje wersję planu,
          ale nie udostępnia go w portalu
          pacjenta.
        </p>
      </header>

      {patient.data.isArchived && (
        <p className="rounded-xl border bg-muted/40 p-4 text-sm">
          Karta jest archiwalna. Plany i historia
          pozostają do odczytu; zmiany wymagają
          przywrócenia karty.
        </p>
      )}

      {create.isError && (
        <TherapyError error={create.error} />
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <label className="flex items-center gap-3 text-sm">
          Status
          <select
            className="h-10 rounded-md border bg-background px-3"
            value={filters.status}
            onChange={(event) =>
              setFilters({
                status: event.target.value,
                page: 1,
              })
            }
          >
            <option value="">
              Wszystkie
            </option>
            {(
              Object.keys(
                planLabels,
              ) as PlanStatus[]
            ).map((status) => (
              <option
                key={status}
                value={status}
              >
                {planLabels[status]}
              </option>
            ))}
          </select>
        </label>

        <Button
          type="button"
          variant="outline"
          disabled={list.isFetching}
          onClick={() =>
            void list.refetch()
          }
        >
          Odśwież listę
        </Button>
      </div>

      {list.isError ? (
        <TherapyError
          error={list.error}
          retry={() => void list.refetch()}
        />
      ) : list.isPending ? (
        <LoadingState title="Pobieranie planów…" />
      ) : (
        <>
          {!list.data.items.length && (
            <p className="rounded-xl border border-dashed p-6">
              Brak planów na tej stronie
              dla wybranego filtra.
            </p>
          )}

          <div className="space-y-4">
            {list.data.items.map((plan) => (
              <article
                key={plan.id}
                className="flex flex-col justify-between gap-4 rounded-2xl border bg-card p-5 sm:flex-row sm:items-center"
              >
                <div className="space-y-3">
                  <h2 className="break-words text-2xl">
                    {plan.name ||
                      'Szkic bez nazwy'}
                  </h2>

                  <div className="flex flex-wrap items-center gap-3">
                    <StatusBadge
                      status={plan.status}
                    />
                    <span className="text-xs text-muted-foreground">
                      Wersja:{' '}
                      {plan.currentVersion}
                    </span>
                  </div>

                  <p className="text-sm text-muted-foreground">
                    {therapyDate(
                      plan.startDate,
                    )}
                    {' – '}
                    {therapyDate(
                      plan.endDate,
                    )}
                  </p>
                </div>

                <Button
                  asChild
                  variant="outline"
                >
                  <Link
                    href={`/patients/${patientId}/therapy-plans/${plan.id}`}
                  >
                    Otwórz plan
                  </Link>
                </Button>
              </article>
            ))}
          </div>

          {(list.data.totalPages > 1 ||
            filters.page > 1) && (
            <Pages
              page={filters.page}
              pages={list.data.totalPages}
              next={list.data.hasNext}
              busy={list.isFetching}
              onChange={(page) =>
                setFilters((old) => ({
                  ...old,
                  page,
                }))
              }
            />
          )}
        </>
      )}
    </div>
  );
}