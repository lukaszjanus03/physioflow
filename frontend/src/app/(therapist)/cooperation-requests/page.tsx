'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Link from 'next/link';
import { useState } from 'react';
import type { FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import {
  COOPERATION_ENDED_BY_LABELS,
  COOPERATION_STATUS_LABELS,
  getCooperationError,
} from '@/lib/cooperation-api';
import type { CooperationStatus } from '@/lib/cooperation-api';
import { therapistCooperationApi } from '@/lib/therapist-cooperation-api';
import type {
  AcceptCooperationPayload,
  CardAction,
  TherapistCooperationRequest,
} from '@/lib/therapist-cooperation-api';
import { useAuthStore } from '@/stores/auth-store';
import type { PatientListItem } from '@/types/api';

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

function patientName(request: TherapistCooperationRequest) {
  return (
    [request.patient.firstName, request.patient.lastName]
      .filter(Boolean)
      .join(' ') || 'Pacjent'
  );
}

export default function CooperationRequestsPage() {
  const { userId, isAuthenticated, roles } = useAuthStore();

  if (!isAuthenticated || !roles.includes('Therapist') || !userId) {
    return null;
  }

  return <RequestsContent key={userId} accountId={userId} />;
}

function RequestsContent({ accountId }: { accountId: string }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<CooperationStatus | 'All'>('Pending');
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [notice, setNotice] = useState('');

  const query = useQuery({
    queryKey: ['therapist-cooperation', accountId, 'list', status, page],
    queryFn: ({ signal }) =>
      therapistCooperationApi.list(status, page, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  async function refreshData() {
    await queryClient.invalidateQueries({
      queryKey: ['therapist-cooperation', accountId],
    });

    await queryClient.invalidateQueries({
      queryKey: ['patients'],
    });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6 lg:p-8">
      <header>
        <span className="text-xs font-semibold uppercase tracking-widest text-primary">
          Współpraca z pacjentami
        </span>
        <h1 className="mt-3 text-4xl sm:text-5xl">Prośby pacjentów</h1>
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          Rozpatruj prośby i łącz konta z właściwymi kartami pacjentów
          po zweryfikowaniu tożsamości.
        </p>
      </header>

      <aside className="rounded-xl border bg-primary/5 p-5 text-sm leading-relaxed">
        Akceptacja prośby tworzy powiązanie konta z kartą, ale nie udostępnia
        automatycznie dokumentacji medycznej. Zgodność imienia, nazwiska
        lub adresu e-mail nie zastępuje weryfikacji tożsamości.
      </aside>

      <div className="flex flex-wrap items-end justify-between gap-4 rounded-xl border bg-card p-5">
        <div className="w-full space-y-2 sm:max-w-xs">
          <Label htmlFor="cooperation-status">Status</Label>
          <select
            id="cooperation-status"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value as CooperationStatus | 'All');
              setPage(1);
              setNotice('');
            }}
            className="h-10 w-full rounded-md border bg-background px-3 text-sm"
          >
            <option value="All">Wszystkie</option>
            {Object.entries(COOPERATION_STATUS_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
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

      {notice && (
        <p
          role="status"
          className="rounded-xl bg-primary/10 p-4 text-sm text-primary"
        >
          {notice}
        </p>
      )}

      {query.isPending ? (
        <p role="status">Wczytywanie próśb…</p>
      ) : query.isError ? (
        <p role="alert" className="rounded-xl border p-5 text-sm text-destructive">
          {getCooperationError(query.error)}
        </p>
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            Liczba próśb: {query.data.totalCount}
          </p>

          {query.data.items.length === 0 ? (
            <section className="rounded-xl border border-dashed p-8 text-center">
              <h2 className="text-2xl">Brak próśb do wyświetlenia</h2>
              <p className="mt-3 text-sm text-muted-foreground">
                Zmień filtr lub odśwież listę później.
              </p>
            </section>
          ) : (
            <div className="space-y-4">
              {query.data.items.map((request) => (
                <article
                  key={request.id}
                  className="rounded-xl border bg-card p-5"
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h2 className="break-words text-2xl">
                        {patientName(request)}
                      </h2>
                      <p className="mt-2 break-words text-sm text-muted-foreground">
                        {request.patient.email}
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

                  <Button
                    type="button"
                    variant="outline"
                    className="mt-4"
                    onClick={() => {
                      setNotice('');
                      setSelectedId(request.id);
                    }}
                  >
                    {request.status === 'Pending'
                      ? 'Rozpatrz prośbę'
                      : 'Zobacz szczegóły'}
                  </Button>
                </article>
              ))}
            </div>
          )}

          {query.data.totalPages > 1 && (
            <nav
              aria-label="Strony próśb"
              className="flex flex-wrap items-center justify-between gap-3"
            >
              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasPrevious || query.isFetching}
                onClick={() => setPage((current) => current - 1)}
              >
                Poprzednia
              </Button>

              <span className="text-sm text-muted-foreground">
                Strona {query.data.pageNumber} z {query.data.totalPages}
              </span>

              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasNext || query.isFetching}
                onClick={() => setPage((current) => current + 1)}
              >
                Następna
              </Button>
            </nav>
          )}

          {page > 1 && query.data.items.length === 0 && (
            <Button type="button" variant="outline" onClick={() => setPage(1)}>
              Wróć na pierwszą stronę
            </Button>
          )}
        </>
      )}

      {selectedId && (
        <RequestDialog
          key={selectedId}
          accountId={accountId}
          requestId={selectedId}
          onClose={() => setSelectedId(null)}
          onChanged={refreshData}
          onSuccess={(message) => {
            setNotice(message);
            setSelectedId(null);
          }}
        />
      )}
    </div>
  );
}

function RequestDialog({
  accountId,
  requestId,
  onClose,
  onChanged,
  onSuccess,
}: {
  accountId: string;
  requestId: string;
  onClose: () => void;
  onChanged: () => Promise<void>;
  onSuccess: (message: string) => void;
}) {
  const [mode, setMode] = useState<'details' | 'accept' | 'reject' | 'end'>(
    'details',
  );
  const [busy, setBusy] = useState(false);

  const query = useQuery({
    queryKey: ['therapist-cooperation', accountId, 'details', requestId],
    queryFn: ({ signal }) =>
      therapistCooperationApi.details(requestId, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open && !busy) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />

        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 max-h-[90dvh] w-[calc(100%-2rem)] max-w-3xl -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border bg-background p-5 shadow-xl sm:p-7"
          onEscapeKeyDown={(event) => {
            if (busy) event.preventDefault();
          }}
          onPointerDownOutside={(event) => {
            if (busy) event.preventDefault();
          }}
        >
          <Dialog.Title className="text-3xl">Prośba o współpracę</Dialog.Title>
          <Dialog.Description className="mt-2 text-sm text-muted-foreground">
            Sprawdź dane zgłoszenia przed podjęciem decyzji.
          </Dialog.Description>

          {query.isPending ? (
            <p role="status" className="mt-6">Wczytywanie szczegółów…</p>
          ) : query.isError ? (
            <div className="mt-6 space-y-4">
              <p role="alert" className="text-sm text-destructive">
                {getCooperationError(query.error)}
              </p>
              <Button
                type="button"
                variant="outline"
                disabled={query.isFetching}
                onClick={() => void query.refetch()}
              >
                Spróbuj ponownie
              </Button>
            </div>
          ) : (
            <div className="mt-6 space-y-5">
              <div className="rounded-xl border p-4">
                <h2 className="text-2xl">{patientName(query.data)}</h2>
                <p className="mt-2 break-words text-sm">
                  {query.data.patient.email}
                </p>
                <p className="mt-3 text-sm text-muted-foreground">
                  Status: {COOPERATION_STATUS_LABELS[query.data.status]}
                </p>
                <p className="mt-2 text-xs text-muted-foreground">
                  Wysłano: {formatDate(query.data.createdAt)}
                </p>
                {query.data.decidedAt && (
                  <p className="mt-2 text-xs text-muted-foreground">
                    Rozpatrzono / wycofano: {formatDate(query.data.decidedAt)}
                  </p>
                )}
                {query.data.endedAt && (
                  <p className="mt-2 text-xs text-muted-foreground">
                    Współpracę zakończono: {formatDate(query.data.endedAt)}
                    {query.data.endedBy &&
                      ` (${COOPERATION_ENDED_BY_LABELS[query.data.endedBy]})`}
                  </p>
                )}
                <p className="mt-4 whitespace-pre-wrap break-words text-sm">
                  {query.data.message || 'Pacjent nie dołączył wiadomości.'}
                </p>
              </div>

              {query.data.status === 'Accepted' && mode === 'details' && (
                <div className="flex flex-wrap gap-3">
                  {query.data.linkedPatientId && (
                    <Button asChild variant="outline">
                      <Link href={`/patients/${query.data.linkedPatientId}`}>
                        Otwórz powiązaną kartę
                      </Link>
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setMode('end')}
                  >
                    Zakończ współpracę
                  </Button>
                </div>
              )}

              {query.data.status === 'Accepted' && mode === 'end' && (
                <EndForm
                  requestId={requestId}
                  onBusyChange={setBusy}
                  onChanged={onChanged}
                  onSuccess={onSuccess}
                  onBack={() => setMode('details')}
                />
              )}

              {query.data.status === 'Pending' && mode === 'details' && (
                <div className="flex flex-wrap gap-3">
                  <Button type="button" onClick={() => setMode('accept')}>
                    Przygotuj akceptację
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setMode('reject')}
                  >
                    Odrzuć prośbę
                  </Button>
                </div>
              )}

              {query.data.status === 'Pending' && mode === 'accept' && (
                <AcceptForm
                  request={query.data}
                  accountId={accountId}
                  onBusyChange={setBusy}
                  onChanged={onChanged}
                  onSuccess={onSuccess}
                  onBack={() => setMode('details')}
                />
              )}

              {query.data.status === 'Pending' && mode === 'reject' && (
                <RejectForm
                  requestId={requestId}
                  onBusyChange={setBusy}
                  onChanged={onChanged}
                  onSuccess={onSuccess}
                  onBack={() => setMode('details')}
                />
              )}
            </div>
          )}

          <div className="mt-6 border-t pt-4">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={onClose}
            >
              Zamknij
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function AcceptForm({
  request,
  accountId,
  onBusyChange,
  onChanged,
  onSuccess,
  onBack,
}: {
  request: TherapistCooperationRequest;
  accountId: string;
  onBusyChange: (busy: boolean) => void;
  onChanged: () => Promise<void>;
  onSuccess: (message: string) => void;
  onBack: () => void;
}) {
  const [cardAction, setCardAction] = useState<CardAction>('LinkExisting');
  const [selectedCard, setSelectedCard] = useState<PatientListItem | null>(null);
  const [identityVerified, setIdentityVerified] = useState(false);
  const [cardConfirmed, setCardConfirmed] = useState(false);
  const [formError, setFormError] = useState('');

  const mutation = useMutation({
    mutationFn: (payload: AcceptCooperationPayload) =>
      therapistCooperationApi.accept(request.id, payload),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => onSuccess('Prośba została zaakceptowana i konto powiązano z kartą.'),
    onSettled: async () => {
      onBusyChange(false);
      await onChanged();
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mutation.isPending) return;

    setFormError('');

    if (!identityVerified || !cardConfirmed) {
      setFormError('Potwierdź weryfikację tożsamości i poprawność wybranej karty.');
      return;
    }

    if (cardAction === 'LinkExisting' && !selectedCard) {
      setFormError('Wybierz istniejącą kartę pacjenta.');
      return;
    }

    const data = new FormData(event.currentTarget);
    const read = (name: string) => String(data.get(name) ?? '').trim();

    mutation.mutate({
      identityVerified: true,
      cardAction,
      patientId: cardAction === 'LinkExisting' ? selectedCard!.id : null,
      newPatient: cardAction === 'CreateNew'
        ? {
            firstName: read('firstName'),
            lastName: read('lastName'),
            pesel: read('pesel') || null,
            dateOfBirth: read('dateOfBirth') || null,
            phoneNumber: read('phoneNumber') || null,
            email: read('email') || null,
          }
        : null,
    });
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-5 rounded-xl border p-4">
      <h2 className="text-2xl">Akceptacja i powiązanie karty</h2>

      <p className="text-sm leading-relaxed text-muted-foreground">
        Najpierw zweryfikuj tożsamość pacjenta w ustalony sposób, np. podczas
        kontaktu z pacjentem w gabinecie. Poniższe zaznaczenie jedynie
        dokumentuje wykonanie weryfikacji — samo jej nie przeprowadza.
      </p>

      <fieldset disabled={mutation.isPending} className="space-y-5">
        <label className="flex items-start gap-3 text-sm">
          <input
            type="checkbox"
            required
            checked={identityVerified}
            onChange={(event) => setIdentityVerified(event.target.checked)}
            className="mt-1 h-4 w-4 shrink-0 accent-teal-600"
          />
          <span>Zweryfikowałem tożsamość osoby korzystającej z tego konta.</span>
        </label>

        <div className="space-y-2">
          <Label htmlFor="card-action">Sposób powiązania</Label>
          <select
            id="card-action"
            value={cardAction}
            onChange={(event) => {
              setCardAction(event.target.value as CardAction);
              setCardConfirmed(false);
              setFormError('');
              mutation.reset();
            }}
            className="h-10 w-full rounded-md border bg-background px-3 text-sm"
          >
            <option value="LinkExisting">Połącz z istniejącą kartą</option>
            <option value="CreateNew">Utwórz nową kartę</option>
          </select>
        </div>

        {cardAction === 'LinkExisting' ? (
          <CardPicker
            accountId={accountId}
            selected={selectedCard}
            onSelect={(card) => {
              setSelectedCard(card);
              setCardConfirmed(false);
              mutation.reset();
            }}
          />
        ) : (
          <div className="space-y-4">
            <p className="text-xs leading-relaxed text-muted-foreground">
              Imię, nazwisko i e-mail pochodzą ze zgłoszenia. Sprawdź je
              przed utworzeniem karty. Jeśli karta już istnieje, wybierz
              powiązanie zamiast tworzenia kolejnej.
            </p>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="new-first-name">Imię *</Label>
                <Input
                  id="new-first-name"
                  name="firstName"
                  required
                  maxLength={100}
                  defaultValue={request.patient.firstName ?? ''}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="new-last-name">Nazwisko *</Label>
                <Input
                  id="new-last-name"
                  name="lastName"
                  required
                  maxLength={100}
                  defaultValue={request.patient.lastName ?? ''}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="new-pesel">PESEL</Label>
                <Input
                  id="new-pesel"
                  name="pesel"
                  inputMode="numeric"
                  pattern="[0-9]{11}"
                  maxLength={11}
                  title="PESEL musi zawierać 11 cyfr."
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="new-birth-date">Data urodzenia</Label>
                <Input id="new-birth-date" name="dateOfBirth" type="date" />
              </div>

              <div className="space-y-2">
                <Label htmlFor="new-email">E-mail</Label>
                <Input
                  id="new-email"
                  name="email"
                  type="email"
                  defaultValue={request.patient.email}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="new-phone">Telefon</Label>
                <Input
                  id="new-phone"
                  name="phoneNumber"
                  type="tel"
                  maxLength={20}
                />
              </div>
            </div>
          </div>
        )}

        <label className="flex items-start gap-3 rounded-lg bg-muted/40 p-4 text-sm">
          <input
            type="checkbox"
            required
            checked={cardConfirmed}
            onChange={(event) => setCardConfirmed(event.target.checked)}
            className="mt-1 h-4 w-4 shrink-0 accent-teal-600"
          />
          <span>
            {cardAction === 'LinkExisting'
              ? 'Sprawdziłem wybraną kartę i potwierdzam, że należy do tej osoby.'
              : 'Sprawdziłem dane i potwierdzam utworzenie nowej karty tej osoby.'}
          </span>
        </label>

        <p className="text-xs leading-relaxed text-muted-foreground">
          Akceptacja nie udostępnia pacjentowi dokumentacji medycznej.
          Backend sprawdzi, czy prośba nadal oczekuje i czy karta może
          zostać powiązana.
        </p>

        {formError && (
          <p role="alert" className="text-sm text-destructive">{formError}</p>
        )}

        {mutation.isError && (
          <p role="alert" className="text-sm text-destructive">
            {getCooperationError(mutation.error)}
          </p>
        )}

        <div className="flex flex-wrap gap-3">
          <Button
            type="submit"
            disabled={
              !identityVerified ||
              !cardConfirmed ||
              (cardAction === 'LinkExisting' && !selectedCard)
            }
          >
            {mutation.isPending ? 'Zapisywanie…' : 'Zaakceptuj i powiąż konto'}
          </Button>
          <Button type="button" variant="outline" onClick={onBack}>
            Wróć
          </Button>
        </div>
      </fieldset>
    </form>
  );
}

function CardPicker({
  accountId,
  selected,
  onSelect,
}: {
  accountId: string;
  selected: PatientListItem | null;
  onSelect: (card: PatientListItem) => void;
}) {
  const [draft, setDraft] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const query = useQuery({
    queryKey: ['cooperation-card-picker', accountId, search, page],
    queryFn: ({ signal }) =>
      therapistCooperationApi.cards(search, page, signal),
    staleTime: 0,
    refetchOnWindowFocus: true,
  });

  return (
    <div className="space-y-4">
      <p className="text-xs leading-relaxed text-muted-foreground">
        Wyświetlamy Twoje aktywne karty. Karty powiązane już z innym kontem
        nie mogą zostać wybrane. Karta nie jest wybierana automatycznie.
      </p>

      <div className="space-y-2">
        <Label htmlFor="card-search">Szukaj karty po danych kontaktowych lub nazwisku</Label>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Input
            id="card-search"
            value={draft}
            maxLength={100}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                setSearch(draft.trim());
                setPage(1);
              }
            }}
            placeholder="Imię, nazwisko, e-mail lub telefon"
          />
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              setSearch(draft.trim());
              setPage(1);
              if (search === draft.trim() && page === 1) {
                void query.refetch();
              }
            }}
          >
            Szukaj
          </Button>
        </div>
      </div>

      {selected && (
        <div role="status" className="rounded-lg bg-primary/10 p-4 text-sm">
          <p className="font-medium">
            Wybrano: {selected.firstName} {selected.lastName}
          </p>
          <p className="mt-1 break-all text-xs text-muted-foreground">
            ID: {selected.id}
          </p>
          <Link
            href={`/patients/${selected.id}`}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-2 inline-block text-primary underline"
          >
            Sprawdź pełną kartę w nowej karcie przeglądarki
          </Link>
        </div>
      )}

      {query.isPending ? (
        <p role="status" className="text-sm">Wczytywanie kart…</p>
      ) : query.isError ? (
        <div className="space-y-3">
          <p role="alert" className="text-sm text-destructive">
            {getCooperationError(query.error)}
          </p>
          <Button
            type="button"
            variant="outline"
            onClick={() => void query.refetch()}
          >
            Spróbuj ponownie
          </Button>
        </div>
      ) : (
        <>
          {query.data.items.length === 0 && (
            <p className="text-sm text-muted-foreground">
              Nie znaleziono kart. Zmień wyszukiwanie albo wybierz utworzenie nowej.
            </p>
          )}

          <div className="max-h-64 space-y-2 overflow-y-auto">
            {query.data.items.map((card) => (
              <label
                key={card.id}
                className={`flex items-start gap-3 rounded-lg border p-3 ${
                  card.isLinkedToAccount
                    ? 'cursor-not-allowed opacity-60'
                    : 'cursor-pointer'
                }`}
              >
                <input
                  type="radio"
                  name="existing-card"
                  checked={selected?.id === card.id}
                  disabled={card.isLinkedToAccount}
                  onChange={() => onSelect(card)}
                  className="mt-1 accent-teal-600"
                />
                <span className="min-w-0 text-sm">
                  <span className="block font-medium">
                    {card.firstName} {card.lastName}
                  </span>
                  {card.isLinkedToAccount && (
                    <span className="mt-1 inline-block rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-700">
                      Powiązana z kontem pacjenta
                    </span>
                  )}
                  <span className="mt-1 block break-words text-xs text-muted-foreground">
                    {card.email || 'Brak e-maila'} · {card.phoneNumber || 'Brak telefonu'}
                  </span>
                  <span className="mt-1 block text-xs text-muted-foreground">
                    Dodano: {formatDate(card.createdAt)}
                  </span>
                </span>
              </label>
            ))}
          </div>

          {query.data.totalPages > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasPrevious || query.isFetching}
                onClick={() => setPage((current) => current - 1)}
              >
                Poprzednie karty
              </Button>
              <span className="text-xs">
                {query.data.pageNumber} / {query.data.totalPages}
              </span>
              <Button
                type="button"
                variant="outline"
                disabled={!query.data.hasNext || query.isFetching}
                onClick={() => setPage((current) => current + 1)}
              >
                Następne karty
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function RejectForm({
  requestId,
  onBusyChange,
  onChanged,
  onSuccess,
  onBack,
}: {
  requestId: string;
  onBusyChange: (busy: boolean) => void;
  onChanged: () => Promise<void>;
  onSuccess: (message: string) => void;
  onBack: () => void;
}) {
  const mutation = useMutation({
    mutationFn: () => therapistCooperationApi.reject(requestId),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => onSuccess('Prośba została odrzucona.'),
    onSettled: async () => {
      onBusyChange(false);
      await onChanged();
    },
  });

  return (
    <section className="space-y-4 rounded-xl border p-4">
      <h2 className="text-2xl">Odrzucić tę prośbę?</h2>
      <p className="text-sm text-muted-foreground">
        Pacjent zobaczy status „Odrzucona”. Nie utworzymy karty
        ani powiązania konta.
      </p>

      {mutation.isError && (
        <p role="alert" className="text-sm text-destructive">
          {getCooperationError(mutation.error)}
        </p>
      )}

      <div className="flex flex-wrap gap-3">
        <Button
          type="button"
          disabled={mutation.isPending}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending ? 'Zapisywanie…' : 'Potwierdź odrzucenie'}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={mutation.isPending}
          onClick={onBack}
        >
          Wróć
        </Button>
      </div>
    </section>
  );
}

function EndForm({
  requestId,
  onBusyChange,
  onChanged,
  onSuccess,
  onBack,
}: {
  requestId: string;
  onBusyChange: (busy: boolean) => void;
  onChanged: () => Promise<void>;
  onSuccess: (message: string) => void;
  onBack: () => void;
}) {
  const mutation = useMutation({
    mutationFn: () => therapistCooperationApi.end(requestId),
    retry: false,
    onMutate: () => onBusyChange(true),
    onSuccess: () => onSuccess('Współpraca została zakończona, a konto odłączone od karty.'),
    onSettled: async () => {
      onBusyChange(false);
      await onChanged();
    },
  });

  return (
    <section className="space-y-4 rounded-xl border p-4">
      <h2 className="text-2xl">Zakończyć współpracę?</h2>
      <p className="text-sm text-muted-foreground">
        Konto pacjenta zostanie odłączone od karty, a udostępnianie
        dokumentacji wyłączone. Karta i cała dokumentacja zostają u Ciebie.
        Pacjent dostanie powiadomienie i może później wysłać nową prośbę.
      </p>

      {mutation.isError && (
        <p role="alert" className="text-sm text-destructive">
          {getCooperationError(mutation.error)}
        </p>
      )}

      <div className="flex flex-wrap gap-3">
        <Button
          type="button"
          disabled={mutation.isPending}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending ? 'Zapisywanie…' : 'Potwierdź zakończenie'}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={mutation.isPending}
          onClick={onBack}
        >
          Wróć
        </Button>
      </div>
    </section>
  );
}