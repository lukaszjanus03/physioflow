'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useRef, useState } from 'react';
import Link from 'next/link';
import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import {
  Calendar,
  Clock,
  FileText,
  History as HistoryIcon,
  MoreHorizontal,
  Pencil,
  RefreshCw,
  ShieldCheck,
  UserRound,
} from 'lucide-react';

import PatientPublicationPanel from '@/components/patient-sharing/PatientPublication';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/primitives';
import { LoadingState } from '@/components/ui/loading-state';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { RecordContentView, RecordFailure } from '@/components/medical-records/MedicalRecordUI';
import { useUnsavedChanges } from '@/hooks/use-unsaved-changes';
import { APPOINTMENT_LABELS } from '@/lib/appointment-display';
import { appointmentsApi } from '@/lib/appointments-api';
import { patientsApi } from '@/lib/api';
import {
  contentForm,
  isRecordId,
  recordDate,
  recordError,
  recordFields,
  recordOptions,
  recordsApi,
  recordStatus,
  validateContent,
} from '@/lib/medical-records-api';
import type { MedicalRecord } from '@/lib/medical-records-api';
import { useAuthStore } from '@/stores/auth-store';

export default function MedicalRecordEditor({
  patientId,
  recordId,
}: {
  patientId: string;
  recordId: string;
}) {
  const accountId = useAuthStore((state) => state.userId);

  const valid = isRecordId(patientId) && isRecordId(recordId);

  const query = useQuery({
    queryKey: ['medical-record', accountId, recordId],
    queryFn: async ({ signal }) => {
      const record = await recordsApi.details(recordId, signal);

      if (record.patientId !== patientId) {
        throw new Error('invalid-patient');
      }

      const patient = await patientsApi.getById(patientId);

      return { record, patient };
    },
    enabled: valid && Boolean(accountId),
    ...recordOptions,
  });

  if (!valid) {
    return <p role="alert">Nieprawidłowy adres dokumentacji.</p>;
  }

  if (query.isError) {
    return <RecordFailure error={query.error} retry={() => void query.refetch()} />;
  }

  if (query.isPending) {
    return <LoadingState title="Pobieranie dokumentacji…" />;
  }

  return (
    <Editor
      key={`${accountId}:${recordId}`}
      accountId={accountId ?? ''}
      initial={query.data.record}
      patientName={`${query.data.patient.firstName} ${query.data.patient.lastName}`}
    />
  );
}

type Operation = 'save' | 'approve' | 'correct';

function Editor({
  initial,
  accountId,
  patientName,
}: {
  initial: MedicalRecord;
  accountId: string;
  patientName: string;
}) {
  const client = useQueryClient();
  const id = useId();
  const lock = useRef(false);

  const [base, setBase] = useState(initial);
  const [sharingBusy, setSharingBusy] = useState(false);
  const [form, setForm] = useState(() => contentForm(initial.content));
  const [correcting, setCorrecting] = useState(false);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState('');
  const [success, setSuccess] = useState('');
  const [conflict, setConflict] = useState(false);
  const [remote, setRemote] = useState<MedicalRecord | null>(null);
  const [loadingRemote, setLoadingRemote] = useState(false);
  const [compared, setCompared] = useState(false);
  const [confirmation, setConfirmation] = useState<'approve' | 'correct' | null>(null);
  const [showHistory, setShowHistory] = useState(false);

  const editable = Boolean(base.appointmentId) && (base.status === 'Draft' || correcting);

  const baseline = contentForm(base.content);

  const dirty =
    editable && (recordFields.some((field) => form[field.key] !== baseline[field.key]) || reason.length > 0);

  const appointment = useQuery({
    queryKey: ['record-appointment', accountId, base.appointmentId],
    queryFn: ({ signal }) => appointmentsApi.details(base.appointmentId!, signal),
    enabled: Boolean(base.appointmentId),
    ...recordOptions,
    refetchOnWindowFocus: true,
  });

  const completed = !appointment.isError && appointment.data?.status === 'Completed';

  async function fetchCurrent() {
    setLoadingRemote(true);
    setCompared(false);

    try {
      const current = await recordsApi.details(base.id);

      if (current.patientId !== base.patientId) {
        throw new Error('invalid-patient');
      }

      setRemote(current);
    } catch (error) {
      setMessage(recordError(error));
    } finally {
      setLoadingRemote(false);
    }
  }

  const mutation = useMutation({
    mutationFn: (operation: Operation) =>
      operation === 'save'
        ? recordsApi.save(base, form)
        : operation === 'approve'
          ? recordsApi.approve(base)
          : recordsApi.correct(base, form, reason),
    retry: false,
    onSuccess: async (record, operation) => {
      setBase(record);
      setForm(contentForm(record.content));
      setCorrecting(false);
      setReason('');
      setRemote(null);
      setConflict(false);
      setErrors({});
      setMessage('');
      setConfirmation(null);

      setSuccess(
        operation === 'save'
          ? 'Szkic zapisany.'
          : operation === 'approve'
            ? 'Dokumentacja zatwierdzona. Zapisano wersję 1.'
            : `Korekta zapisana jako wersja ${record.currentVersion}.`,
      );

      await Promise.all([
        client.invalidateQueries({
          queryKey: ['medical-records', accountId, record.patientId],
        }),
        client.invalidateQueries({
          queryKey: ['medical-record-history', accountId, record.id],
        }),
        client.invalidateQueries({
          queryKey: ['appointment-documentation', accountId, record.patientId],
        }),
      ]);
    },
    onError: async (error) => {
      setConfirmation(null);
      setMessage(recordError(error));

      if (recordStatus(error) === 409) {
        setConflict(true);
        await fetchCurrent();
      }
    },
    onSettled: () => {
      lock.current = false;
    },
  });

  const busy = mutation.isPending || loadingRemote || sharingBusy;

  const guard = useUnsavedChanges(dirty, busy);

  function validate(operation: Operation) {
    const next = validateContent(form, operation !== 'save', operation === 'correct' ? reason : undefined);

    setErrors(next);

    if (Object.keys(next).length) {
      setMessage('Sprawdź zaznaczone pola formularza.');
      return false;
    }

    if (operation === 'approve' && dirty) {
      setMessage('Najpierw zapisz szkic. Zatwierdzenie dotyczy wyłącznie treści zapisanej na serwerze.');
      return false;
    }

    if (operation !== 'save' && !completed) {
      setMessage('Oznacz wizytę jako odbytą i odśwież jej status.');
      return false;
    }

    return true;
  }

  function send(operation: Operation) {
    if (lock.current || busy || conflict || remote || !base.appointmentId || !validate(operation)) {
      return;
    }

    lock.current = true;
    setMessage('');
    setSuccess('');
    mutation.mutate(operation);
  }

  function requestConfirmation(operation: 'approve' | 'correct') {
    if (!busy && !conflict && !remote && validate(operation)) {
      setConfirmation(operation);
    }
  }

  function acceptRemote(keepLocal: boolean) {
    if (!remote || busy || (keepLocal && !compared)) {
      return;
    }

    if (!keepLocal) {
      setForm(contentForm(remote.content));
      setReason('');
      setCorrecting(false);
    } else {
      setCorrecting(remote.status === 'Approved');
    }

    setBase(remote);
    setRemote(null);
    setConflict(false);
    setErrors({});
    setMessage('');
    setSuccess('');
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 sm:p-6">
      {guard.discardDialog}

      <div className="flex flex-wrap gap-3">
        <Button asChild variant="outline">
          <Link href={`/patients/${base.patientId}/documentation`}>Historia dokumentacji</Link>
        </Button>

        <Button asChild variant="ghost">
          <Link href={`/patients/${base.patientId}`}>Karta pacjenta</Link>
        </Button>
      </div>

      <header className="overflow-hidden rounded-2xl border bg-card shadow-sm">
        <div className="border-b bg-gradient-to-br from-primary/10 via-card to-card p-5 sm:p-7">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex items-center gap-3">
              <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                <FileText aria-hidden="true" className="h-6 w-6" />
              </span>

              <div>
                <p className="mb-1 text-xs uppercase tracking-widest text-muted-foreground">
                  Dokumentacja terapii
                </p>

                <h1 className="text-3xl sm:text-4xl">Dokumentacja wizyty</h1>
              </div>
            </div>

            <div className="flex flex-wrap gap-2">
              <span
                className={`inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium ${
                  base.status === 'Approved' ? 'bg-emerald-50 text-emerald-800' : 'bg-amber-50 text-amber-800'
                }`}
              >
                {base.status === 'Approved' && <ShieldCheck aria-hidden="true" className="h-3.5 w-3.5" />}

                {base.status === 'Draft' ? 'Szkic' : 'Zatwierdzona'}
              </span>

              {base.currentVersion > 0 && (
                <span className="rounded-full border bg-card px-3 py-1.5 text-xs text-muted-foreground">
                  Wersja {base.currentVersion}
                </span>
              )}

              {correcting && (
                <span className="rounded-full bg-primary/10 px-3 py-1.5 text-xs text-primary">
                  Korekta w przygotowaniu
                </span>
              )}
            </div>
          </div>

          <dl className="mt-6 grid gap-5 sm:grid-cols-3">
            <div>
              <dt className="flex items-center gap-2 text-xs text-muted-foreground">
                <UserRound aria-hidden="true" className="h-4 w-4" />
                Pacjent
              </dt>

              <dd className="mt-2 break-words font-medium">{patientName}</dd>
            </div>

            <div>
              <dt className="flex items-center gap-2 text-xs text-muted-foreground">
                <Calendar aria-hidden="true" className="h-4 w-4" />
                Termin wizyty
              </dt>

              <dd className="mt-2 text-sm">
                {!base.appointmentId
                  ? 'Starszy wpis bez wizyty'
                  : appointment.isError
                    ? 'Nie udało się pobrać wizyty'
                    : appointment.data
                      ? recordDate(appointment.data.startsAt)
                      : 'Wczytywanie…'}
              </dd>

              {base.appointmentId && !appointment.isError && appointment.data && (
                <dd className="mt-1 text-xs text-muted-foreground">
                  {APPOINTMENT_LABELS[appointment.data.status]}
                </dd>
              )}
            </div>

            <div>
              <dt className="flex items-center gap-2 text-xs text-muted-foreground">
                <Clock aria-hidden="true" className="h-4 w-4" />
                Ostatnia aktualizacja
              </dt>

              <dd className="mt-2 text-sm">{recordDate(base.updatedAt ?? base.createdAt)}</dd>
            </div>
          </dl>
        </div>

        <div className="flex flex-wrap items-center gap-3 px-5 py-4 sm:px-7">
          {!editable && base.appointmentId && base.status === 'Approved' && (
            <Button
              type="button"
              disabled={busy || !completed || conflict || Boolean(remote)}
              onClick={() => {
                setCorrecting(true);
                setMessage('');
                setSuccess('');
              }}
            >
              <Pencil aria-hidden="true" className="mr-2 h-4 w-4" />
              Wprowadź korektę
            </Button>
          )}

          <Button
            type="button"
            variant="outline"
            aria-expanded={showHistory}
            aria-controls={`${id}-history`}
            disabled={busy}
            onClick={() => setShowHistory((value) => !value)}
          >
            <HistoryIcon aria-hidden="true" className="mr-2 h-4 w-4" />
            {showHistory ? 'Ukryj historię wersji' : 'Historia wersji'}
          </Button>

          <DropdownMenu.Root>
            <DropdownMenu.Trigger asChild>
              <Button type="button" variant="ghost" disabled={busy}>
                <MoreHorizontal aria-hidden="true" className="mr-2 h-4 w-4" />
                Więcej działań
              </Button>
            </DropdownMenu.Trigger>

            <DropdownMenu.Portal>
              <DropdownMenu.Content
                align="start"
                sideOffset={8}
                className="bg-popover text-popover-foreground z-[60] w-72 max-w-[calc(100vw-2rem)] rounded-xl border p-1.5 shadow-lg"
              >
                <DropdownMenu.Item
                  disabled={busy}
                  onSelect={() => void fetchCurrent()}
                  className="flex cursor-pointer items-center gap-2 rounded-lg px-3 py-3 text-sm outline-none focus:bg-accent data-[disabled]:pointer-events-none data-[disabled]:opacity-50"
                >
                  <FileText aria-hidden="true" className="h-4 w-4 shrink-0" />
                  Porównaj z aktualnym wpisem
                </DropdownMenu.Item>

                {base.appointmentId && (
                  <DropdownMenu.Item
                    disabled={busy || appointment.isFetching}
                    onSelect={() => void appointment.refetch()}
                    className="flex cursor-pointer items-center gap-2 rounded-lg px-3 py-3 text-sm outline-none focus:bg-accent data-[disabled]:pointer-events-none data-[disabled]:opacity-50"
                  >
                    <RefreshCw aria-hidden="true" className="h-4 w-4 shrink-0" />
                    Odśwież status wizyty
                  </DropdownMenu.Item>
                )}
              </DropdownMenu.Content>
            </DropdownMenu.Portal>
          </DropdownMenu.Root>

          {editable && (
            <p role="status" className="w-full text-xs text-muted-foreground">
              {dirty
                ? 'Niezapisane zmiany. Zapis jest ręczny.'
                : 'Brak niezapisanych zmian. Zapis jest ręczny.'}
            </p>
          )}
        </div>
      </header>

      <PatientPublicationPanel
        key={`${accountId}:${base.id}:${base.version}`}
        kind="record"
        entity={base}
        busy={busy}
        locked={dirty || correcting || conflict || Boolean(remote)}
        canPublish={base.status === 'Approved' && completed}
        onBusyChange={setSharingBusy}
        onUpdated={(value) => {
          setBase(value);
          setForm(contentForm(value.content));
          setMessage('');
          setSuccess('');
        }}
        onConflict={() => {
          setConflict(true);
          void fetchCurrent();
        }}
      />
      {!base.appointmentId && (
        <p className="rounded-xl border bg-muted/40 p-4 text-sm text-muted-foreground">
          Starszy wpis bez powiązanej wizyty. Dostępny wyłącznie do odczytu.
        </p>
      )}

      {base.appointmentId && appointment.isError && (
        <RecordFailure error={appointment.error} retry={() => void appointment.refetch()} />
      )}

      {base.appointmentId && !appointment.isError && appointment.data && !completed && (
        <p className="rounded-xl border border-amber-200 bg-amber-50/50 p-4 text-sm">
          Zatwierdzenie i korekta wymagają wizyty oznaczonej jako odbyta. Status możesz odświeżyć w menu
          „Więcej działań”.
        </p>
      )}

      {loadingRemote && (
        <p role="status" className="text-sm text-muted-foreground">
          Pobieranie aktualnego wpisu do porównania…
        </p>
      )}

      {remote && (
        <section className="space-y-4 rounded-2xl border border-amber-400 bg-amber-50/40 p-5">
          <h2 className="text-3xl">Porównaj z aktualnym wpisem</h2>

          <p className="text-sm">
            Poniżej jest aktualna treść serwera. Twoja lokalna treść pozostała w formularzu pod porównaniem.
            Status serwera:{' '}
            {remote.status === 'Draft' ? 'Szkic' : `Zatwierdzona, wersja ${remote.currentVersion}`}.
          </p>

          <details>
            <summary className="cursor-pointer font-medium">Pokaż pełną aktualną treść serwera</summary>

            <div className="mt-4">
              <RecordContentView content={remote.content} />
            </div>
          </details>

          <label className="flex items-start gap-3 text-sm">
            <input
              type="checkbox"
              checked={compared}
              onChange={(event) => setCompared(event.target.checked)}
              className="mt-1"
            />
            Porównałem obie treści. Świadomie wybieram lokalną treść jako pełny zamiennik aktualnej treści
            serwera.
          </label>

          <div className="flex flex-wrap gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() => guard.confirmLeave(() => acceptRemote(false))}
            >
              Przyjmij treść serwera i odrzuć lokalne zmiany
            </Button>

            {remote.appointmentId && (
              <Button type="button" disabled={busy || !compared} onClick={() => acceptRemote(true)}>
                Zachowaj lokalną treść do dalszej edycji
              </Button>
            )}
          </div>

          <p className="text-xs text-muted-foreground">
            Żaden przycisk nie zapisuje automatycznie. Jeśli wpis jest już zatwierdzony, dalszy zapis lokalnej
            treści wymaga korekty i uzasadnienia.
          </p>
        </section>
      )}

      {editable ? (
        <form
          className="space-y-5"
          aria-busy={busy}
          onSubmit={(event) => {
            event.preventDefault();

            if (correcting) {
              requestConfirmation('correct');
            } else {
              send('save');
            }
          }}
        >
          <div className="grid gap-5 lg:grid-cols-2">
            {recordFields.map((field) => (
              <section
                key={field.key}
                className={`space-y-3 rounded-xl border bg-card p-5 ${
                  field.key === 'patientSummary' ? 'border-primary/30 lg:col-span-2' : ''
                }`}
              >
                <label htmlFor={`${id}-${field.key}`} className="font-medium">
                  {field.label}
                  {correcting && field.key !== 'patientSummary' ? ' *' : ''}
                </label>

                <Textarea
                  id={`${id}-${field.key}`}
                  value={form[field.key]}
                  maxLength={field.limit}
                  rows={7}
                  disabled={busy}
                  aria-invalid={Boolean(errors[field.key])}
                  aria-describedby={`${id}-${field.key}-help`}
                  onChange={(event) => {
                    const value = event.target.value;

                    setForm((old) => ({
                      ...old,
                      [field.key]: value,
                    }));

                    setSuccess('');
                  }}
                />

                <p
                  id={`${id}-${field.key}-help`}
                  className={`text-xs ${errors[field.key] ? 'text-destructive' : 'text-muted-foreground'}`}
                >
                  {errors[field.key] || `${form[field.key].length}/${field.limit}`}
                </p>

                {field.key === 'patientSummary' && (
                  <p className="text-xs text-muted-foreground">
                    Opcjonalne. Pacjent zobaczy je dopiero po świadomym opublikowaniu wybranej zatwierdzonej
                    wersji. Pozostałe pola są wewnętrzną dokumentacją terapeuty.
                  </p>
                )}
              </section>
            ))}
          </div>

          {correcting && (
            <section className="space-y-3 rounded-xl border bg-card p-5">
              <label htmlFor={`${id}-reason`} className="font-medium">
                Powód korekty *
              </label>

              <Textarea
                id={`${id}-reason`}
                value={reason}
                maxLength={1000}
                disabled={busy}
                aria-invalid={Boolean(errors.correctionReason)}
                aria-describedby={`${id}-reason-help`}
                onChange={(event) => setReason(event.target.value)}
              />

              <p
                id={`${id}-reason-help`}
                className={`text-xs ${
                  errors.correctionReason ? 'text-destructive' : 'text-muted-foreground'
                }`}
              >
                {errors.correctionReason ||
                  `${reason.length}/1000. Powód zostanie zapisany w historii wersji.`}
              </p>
            </section>
          )}

          <div className="flex flex-wrap gap-3">
            <Button
              type="submit"
              disabled={busy || conflict || Boolean(remote) || (correcting && !completed)}
            >
              {busy ? 'Zapisywanie…' : correcting ? 'Zapisz korektę jako nową wersję' : 'Zapisz szkic'}
            </Button>

            {correcting ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() =>
                  guard.confirmLeave(() => {
                    setForm(contentForm(base.content));
                    setReason('');
                    setCorrecting(false);
                    setErrors({});
                  })
                }
              >
                Anuluj korektę
              </Button>
            ) : (
              <Button
                type="button"
                variant="outline"
                disabled={busy || dirty || !completed || conflict || Boolean(remote)}
                onClick={() => requestConfirmation('approve')}
              >
                Zatwierdź zapisany szkic
              </Button>
            )}
          </div>

          {!correcting && dirty && (
            <p className="text-sm text-muted-foreground">Przed zatwierdzeniem zapisz szkic.</p>
          )}
        </form>
      ) : (
        <RecordContentView content={base.content} />
      )}

      {message && (
        <p role="alert" className="whitespace-pre-wrap text-sm text-destructive">
          {message}
        </p>
      )}

      {success && (
        <p role="status" className="text-sm text-primary">
          {success}
        </p>
      )}

      {conflict && !remote && !busy && (
        <Button type="button" variant="outline" onClick={() => void fetchCurrent()}>
          Pobierz aktualny wpis po konflikcie
        </Button>
      )}

      {showHistory && (
        <section id={`${id}-history`} className="space-y-4 border-t pt-6">
          <h2 className="text-3xl">Historia zatwierdzonych wersji</h2>

          <History accountId={accountId} recordId={base.id} />
        </section>
      )}

      <Dialog
        open={confirmation !== null}
        onOpenChange={(open) => {
          if (!open && !busy) {
            setConfirmation(null);
          }
        }}
      >
        <DialogContent className="w-[calc(100%-2rem)] rounded-2xl">
          <DialogHeader>
            <DialogTitle>
              {confirmation === 'approve' ? 'Zatwierdzić zapisany szkic?' : 'Zapisać korektę dokumentacji?'}
            </DialogTitle>

            <DialogDescription>
              {confirmation === 'approve'
                ? 'Zostanie utworzona pierwsza trwała wersja. Dalsze zmiany będą wymagały korekty z uzasadnieniem.'
                : 'Pełna treść formularza zostanie zapisana jako kolejna zatwierdzona wersja. Poprzednia wersja pozostanie w historii.'}
            </DialogDescription>
          </DialogHeader>

          <DialogFooter>
            <Button type="button" variant="outline" disabled={busy} onClick={() => setConfirmation(null)}>
              Anuluj
            </Button>

            <Button
              type="button"
              disabled={busy}
              onClick={() => {
                if (confirmation) {
                  send(confirmation);
                }
              }}
            >
              {busy ? 'Zapisywanie…' : 'Potwierdź'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function History({ accountId, recordId }: { accountId: string; recordId: string }) {
  const [selected, setSelected] = useState<number | null>(null);

  const query = useQuery({
    queryKey: ['medical-record-history', accountId, recordId],
    queryFn: ({ signal }) => recordsApi.history(recordId, signal),
    ...recordOptions,
  });

  if (query.isError) {
    return <RecordFailure error={query.error} retry={() => void query.refetch()} />;
  }

  if (query.isPending) {
    return <LoadingState title="Pobieranie historii wersji…" />;
  }

  const version = query.data.find((item) => item.versionNumber === selected);

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Historia zawiera wersje zatwierdzone. Kolejne zapisy szkicu nie tworzą tu wpisów.
      </p>

      {!query.data.length && <p>Brak zatwierdzonych wersji.</p>}

      <div className="flex flex-wrap gap-3">
        {query.data.map((item) => (
          <Button
            key={item.versionNumber}
            type="button"
            variant={selected === item.versionNumber ? 'default' : 'outline'}
            aria-pressed={selected === item.versionNumber}
            onClick={() => setSelected(item.versionNumber)}
          >
            Wersja {item.versionNumber}
          </Button>
        ))}
      </div>

      {version && (
        <article className="space-y-4 rounded-xl border bg-card p-5">
          <h2 className="text-3xl">Wersja {version.versionNumber}</h2>

          <p className="text-sm">Zapisano: {recordDate(version.recordedAt)}</p>

          <p className="break-all text-xs text-muted-foreground">
            Autor: {version.authorUserId === accountId ? 'Twoje konto' : version.authorUserId}
          </p>

          <p className="whitespace-pre-wrap break-words text-sm">
            {version.correctionReason
              ? `Powód korekty: ${version.correctionReason}`
              : 'Pierwsze zatwierdzenie dokumentacji.'}
          </p>

          <RecordContentView content={version.content} />
        </article>
      )}
    </div>
  );
}
