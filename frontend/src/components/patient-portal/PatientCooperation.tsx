'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { Label, Textarea } from '@/components/ui/primitives';
import {
  COOPERATION_ENDED_BY_LABELS,
  COOPERATION_STATUS_LABELS,
  cooperationApi,
  cooperationKeys,
  getCooperationError,
} from '@/lib/cooperation-api';
import type {
  CooperationStatus,
  PatientCooperation,
  PatientCooperationRequest,
} from '@/lib/cooperation-api';

const statusStyles: Record<CooperationStatus, string> = {
  Pending: 'bg-amber-50 text-amber-800',
  Accepted: 'bg-emerald-50 text-emerald-800',
  Rejected: 'bg-red-50 text-red-800',
  Withdrawn: 'bg-slate-100 text-slate-700',
  Ended: 'bg-slate-100 text-slate-700',
};

function formatDate(value: string) {
  return new Date(value).toLocaleString('pl-PL', {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

function usePatientRequests(accountId: string) {
  return useQuery({
    queryKey: cooperationKeys.requests(accountId),
    queryFn: ({ signal }) => cooperationApi.requests(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });
}

function Confirmation({
  open,
  onOpenChange,
  title,
  description,
  children,
  pending,
  onConfirm,
  confirmLabel,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  children?: ReactNode;
  pending: boolean;
  onConfirm: () => void;
  confirmLabel: string;
}) {
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (!pending) onOpenChange(nextOpen);
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 max-h-[90dvh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border bg-background p-6 shadow-xl"
          onEscapeKeyDown={(event) => {
            if (pending) event.preventDefault();
          }}
          onPointerDownOutside={(event) => {
            if (pending) event.preventDefault();
          }}
        >
          <Dialog.Title className="text-3xl">{title}</Dialog.Title>

          <Dialog.Description className="mt-3 text-sm leading-relaxed text-muted-foreground">
            {description}
          </Dialog.Description>

          {children}

          <div className="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Dialog.Close asChild>
              <Button type="button" variant="outline" disabled={pending}>
                Anuluj
              </Button>
            </Dialog.Close>

            <Button type="button" disabled={pending} onClick={onConfirm}>
              {pending ? 'Przetwarzanie…' : confirmLabel}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function SendCooperationRequest({
  accountId,
  therapistId,
  therapistName,
}: {
  accountId: string;
  therapistId: string;
  therapistName: string;
}) {
  const queryClient = useQueryClient();
  const requests = usePatientRequests(accountId);
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState('');

  const mutation = useMutation({
    mutationFn: () => cooperationApi.send(therapistId, message),
    retry: false,
    onSuccess: () => {
      setOpen(false);
      setMessage('');
    },
    onSettled: async () => {
      await queryClient.invalidateQueries({
        queryKey: cooperationKeys.all(accountId),
      });
    },
  });

  const existing = requests.data?.find(
    (request) =>
      request.therapist.id === therapistId &&
      (request.status === 'Pending' || request.status === 'Accepted'),
  );

  return (
    <div className="mt-6 space-y-4 border-t pt-6">
      <h2 className="text-2xl">Rozpocznij współpracę</h2>

      <p className="text-sm leading-relaxed text-muted-foreground">
        Wyślij prośbę do terapeuty. Terapeuta zweryfikuje Twoją tożsamość
        przed powiązaniem konta z kartą pacjenta. Nie jest to rezerwacja wizyty
        ani automatyczne udostępnienie dokumentacji.
      </p>

      {requests.isPending ? (
        <p role="status" className="text-sm text-muted-foreground">
          Sprawdzanie aktualnych próśb…
        </p>
      ) : requests.isError ? (
        <div role="alert" className="space-y-3">
          <p className="text-sm text-destructive">
            {getCooperationError(requests.error)}
          </p>

          <Button
            type="button"
            variant="outline"
            disabled={requests.isFetching}
            onClick={() => void requests.refetch()}
          >
            Sprawdź ponownie
          </Button>
        </div>
      ) : existing ? (
        <p
          role="status"
          className="rounded-xl bg-primary/10 p-4 text-sm text-primary"
        >
          {existing.status === 'Accepted'
            ? 'Współpracujesz już z tym terapeutą.'
            : 'Twoja prośba oczekuje na decyzję terapeuty.'}
        </p>
      ) : (
        <Button
          type="button"
          disabled={requests.isFetching || mutation.isPending}
          onClick={() => {
            mutation.reset();
            setOpen(true);
          }}
        >
          Wyślij prośbę o współpracę
        </Button>
      )}

      {mutation.isSuccess && (
        <p role="status" className="text-sm text-primary">
          Prośba została wysłana. Jej status znajdziesz w „Moje prośby”.
        </p>
      )}

      <Confirmation
        open={open}
        onOpenChange={setOpen}
        title="Prośba o współpracę"
        description={`Odbiorca: ${therapistName}. Terapeuta otrzyma Twoje imię, nazwisko, adres e-mail i opcjonalną wiadomość.`}
        pending={mutation.isPending}
        confirmLabel="Potwierdź i wyślij"
        onConfirm={() => mutation.mutate()}
      >
        <div className="mt-5 space-y-2">
          <Label htmlFor="cooperation-message">
            Wiadomość do terapeuty — opcjonalnie
          </Label>

          <Textarea
            id="cooperation-message"
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            maxLength={500}
            rows={4}
            disabled={mutation.isPending}
            placeholder="Np. Chcę rozpocząć współpracę w Państwa gabinecie."
            aria-describedby="cooperation-message-help"
          />

          <p
            id="cooperation-message-help"
            className="text-xs leading-relaxed text-muted-foreground"
          >
            {message.length}/500 znaków. Nie wpisuj PESEL-u ani szczegółowej
            historii medycznej.
          </p>
        </div>

        {mutation.isError && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {getCooperationError(mutation.error)}
          </p>
        )}
      </Confirmation>
    </div>
  );
}

export function MyCooperationRequests({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient();
  const query = usePatientRequests(accountId);
  const [filter, setFilter] = useState<CooperationStatus | 'All'>('All');
  const [selected, setSelected] = useState<PatientCooperationRequest | null>(
    null,
  );

  const mutation = useMutation({
    mutationFn: (requestId: string) => cooperationApi.withdraw(requestId),
    retry: false,
    onSuccess: () => setSelected(null),
    onSettled: async () => {
      await queryClient.invalidateQueries({
        queryKey: cooperationKeys.all(accountId),
      });
    },
  });

  const items = query.data?.filter(
    (request) => filter === 'All' || request.status === filter,
  );

  return (
    <section className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-4xl">Moje prośby</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Sprawdzaj decyzje terapeutów i zarządzaj oczekującymi prośbami.
          </p>
        </div>

        <Button
          type="button"
          variant="outline"
          disabled={query.isFetching || mutation.isPending}
          onClick={() => void query.refetch()}
        >
          {query.isFetching ? 'Odświeżanie…' : 'Odśwież'}
        </Button>
      </div>

      <div className="space-y-2">
        <Label htmlFor="request-status">Status prośby</Label>
        <select
          id="request-status"
          value={filter}
          onChange={(event) =>
            setFilter(event.target.value as CooperationStatus | 'All')
          }
          className="block h-10 w-full rounded-md border bg-background px-3 text-sm sm:max-w-xs"
        >
          <option value="All">Wszystkie</option>
          {Object.entries(COOPERATION_STATUS_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </div>

      {query.isPending ? (
        <p role="status">Wczytywanie próśb…</p>
      ) : query.isError ? (
        <p role="alert" className="rounded-xl border p-5 text-sm text-destructive">
          {getCooperationError(query.error)}
        </p>
      ) : items?.length === 0 ? (
        <div className="rounded-xl border border-dashed p-8 text-center">
          <h2 className="text-2xl">Brak próśb do wyświetlenia</h2>
          <p className="mt-3 text-sm text-muted-foreground">
            Zmień filtr lub wybierz terapeutę w zakładce „Znajdź terapeutę”.
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {items?.map((request) => (
            <article key={request.id} className="rounded-xl border bg-card p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h2 className="text-2xl">
                    {request.therapist.firstName} {request.therapist.lastName}
                  </h2>
                  <p className="mt-2 text-sm text-muted-foreground">
                    {[
                      request.therapist.practiceName,
                      request.therapist.city,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </p>
                </div>

                <span
                  className={`rounded-full px-3 py-1 text-xs font-medium ${statusStyles[request.status]}`}
                >
                  {COOPERATION_STATUS_LABELS[request.status]}
                </span>
              </div>

              <p className="mt-4 text-xs text-muted-foreground">
                Wysłano: {formatDate(request.createdAt)}
              </p>

              {request.decidedAt && (
                <p className="mt-1 text-xs text-muted-foreground">
                  Rozpatrzono / wycofano: {formatDate(request.decidedAt)}
                </p>
              )}

              {request.endedAt && (
                <p className="mt-1 text-xs text-muted-foreground">
                  Współpracę zakończono: {formatDate(request.endedAt)}
                  {request.endedBy &&
                    ` (${COOPERATION_ENDED_BY_LABELS[request.endedBy]})`}
                </p>
              )}

              {request.message && (
                <div className="mt-4 rounded-lg bg-muted/30 p-4">
                  <p className="text-xs text-muted-foreground">
                    Twoja wiadomość
                  </p>
                  <p className="mt-2 whitespace-pre-wrap break-words text-sm">
                    {request.message}
                  </p>
                </div>
              )}

              {request.status === 'Pending' && (
                <Button
                  type="button"
                  variant="outline"
                  className="mt-4"
                  disabled={mutation.isPending}
                  onClick={() => {
                    mutation.reset();
                    setSelected(request);
                  }}
                >
                  Wycofaj prośbę
                </Button>
              )}
            </article>
          ))}
        </div>
      )}

      {mutation.isSuccess && (
        <p role="status" className="text-sm text-primary">
          Prośba została wycofana.
        </p>
      )}

      <Confirmation
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
        title="Wycofać prośbę?"
        description={`Terapeuta ${selected?.therapist.firstName ?? ''} ${selected?.therapist.lastName ?? ''} nie będzie już mógł jej zaakceptować. Wycofanie jest możliwe tylko dla oczekującej prośby.`}
        pending={mutation.isPending}
        confirmLabel="Wycofaj prośbę"
        onConfirm={() => {
          if (selected) mutation.mutate(selected.id);
        }}
      >
        {mutation.isError && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {getCooperationError(mutation.error)}
          </p>
        )}
      </Confirmation>
    </section>
  );
}

export function MyTherapists({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: cooperationKeys.therapists(accountId),
    queryFn: ({ signal }) => cooperationApi.therapists(signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });
  const [selected, setSelected] = useState<PatientCooperation | null>(null);

  const mutation = useMutation({
    mutationFn: (requestId: string) => cooperationApi.end(requestId),
    retry: false,
    onSuccess: () => setSelected(null),
    onSettled: async () => {
      await queryClient.invalidateQueries({
        queryKey: cooperationKeys.all(accountId),
      });
    },
  });

  return (
    <section className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-4xl">Moi terapeuci</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Terapeuci, którzy zaakceptowali Twoją prośbę o współpracę.
          </p>
        </div>

        <Button
          type="button"
          variant="outline"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          {query.isFetching ? 'Odświeżanie…' : 'Odśwież'}
        </Button>
      </div>

      {query.isPending ? (
        <p role="status">Wczytywanie współprac…</p>
      ) : query.isError ? (
        <p role="alert" className="rounded-xl border p-5 text-sm text-destructive">
          {getCooperationError(query.error)}
        </p>
      ) : query.data.length === 0 ? (
        <div className="rounded-xl border border-dashed p-8 text-center">
          <h2 className="text-2xl">Nie masz aktywnej współpracy</h2>
          <p className="mt-3 text-sm text-muted-foreground">
            Znajdź terapeutę i wyślij prośbę. Pojawi się tutaj po jej akceptacji.
          </p>
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2">
          {query.data.map((cooperation) => (
            <article
              key={cooperation.requestId}
              className="rounded-xl border bg-card p-6"
            >
              <span className="rounded-full bg-primary/10 px-3 py-1 text-xs text-primary">
                Aktywna współpraca
              </span>

              <h2 className="mt-4 text-3xl">
                {cooperation.therapist.firstName}{' '}
                {cooperation.therapist.lastName}
              </h2>

              <p className="mt-3 text-sm text-muted-foreground">
                {[
                  cooperation.therapist.practiceName,
                  cooperation.therapist.city,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </p>

              <p className="mt-4 text-xs text-muted-foreground">
                Współpraca od: {formatDate(cooperation.since)}
              </p>

              <Button
                type="button"
                variant="outline"
                className="mt-4"
                disabled={mutation.isPending}
                onClick={() => {
                  mutation.reset();
                  setSelected(cooperation);
                }}
              >
                Zakończ współpracę
              </Button>
            </article>
          ))}
        </div>
      )}

      {mutation.isSuccess && (
        <p role="status" className="text-sm text-primary">
          Współpraca została zakończona.
        </p>
      )}

      <Confirmation
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) setSelected(null);
        }}
        title="Zakończyć współpracę?"
        description={`Twoje konto zostanie odłączone od karty pacjenta u terapeuty ${selected?.therapist.firstName ?? ''} ${selected?.therapist.lastName ?? ''}. Terapeuta zachowa swoją dokumentację. Współpracę możesz później wznowić, wysyłając nową prośbę.`}
        pending={mutation.isPending}
        confirmLabel="Zakończ współpracę"
        onConfirm={() => {
          if (selected) mutation.mutate(selected.requestId);
        }}
      >
        {mutation.isError && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {getCooperationError(mutation.error)}
          </p>
        )}
      </Confirmation>

      <aside className="rounded-xl border bg-muted/30 p-5 text-sm leading-relaxed text-muted-foreground">
        Powiązanie z terapeutą nie udostępnia automatycznie dokumentacji
        medycznej. Każdy terapeuta prowadzi osobną kartę pacjenta.
      </aside>
    </section>
  );
}