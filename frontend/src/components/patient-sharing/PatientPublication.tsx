'use client';

import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Eye, Loader2, Share2, ShieldCheck, ShieldOff } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { PlanView } from '@/components/therapy/TherapyUI';
import { patientsApi } from '@/lib/api';
import { recordsApi, recordDate } from '@/lib/medical-records-api';
import type { MedicalRecord } from '@/lib/medical-records-api';
import { therapyApi, therapyOptions } from '@/lib/therapy-api';
import type { TherapyPlan, PlanContent } from '@/lib/therapy-api';
import { patientSharingApi, sharingError, needsSharingRefresh } from '@/lib/patient-sharing-api';
import type { PatientPublication } from '@/lib/patient-sharing-api';
import { useAuthStore } from '@/stores/auth-store';

export function PublicationBadge({ publication }: { publication?: PatientPublication | null }) {
  if (!publication) return <span className="text-xs text-muted-foreground">Nieudostępnione pacjentowi</span>;
  return (
    <span
      className={`inline-flex flex-wrap items-center gap-1.5 rounded-full px-3 py-1 text-xs ${publication.isAccessible ? 'bg-primary/10 text-primary' : 'bg-amber-50 text-amber-800'}`}
    >
      <ShieldCheck size={13} aria-hidden="true" />
      Udostępniono wersję {publication.versionNumber}
      {!publication.isAccessible && ' · nieaktualne powiązanie konta'}
    </span>
  );
}

type Common = {
  busy: boolean;
  locked: boolean;
  canPublish: boolean;
  onBusyChange: (busy: boolean) => void;
  onConflict: () => void;
};
type Props = Common &
  (
    | { kind: 'plan'; entity: TherapyPlan; onUpdated: (value: TherapyPlan) => void }
    | { kind: 'record'; entity: MedicalRecord; onUpdated: (value: MedicalRecord) => void }
  );
type Preview = { number: number; plan?: PlanContent; summary?: string | null };

export default function PatientPublicationPanel(props: Props) {
  const accountId = useAuthStore((state) => state.userId);
  const client = useQueryClient();
  const { entity } = props;
  const [number, setNumber] = useState(entity.publication?.versionNumber ?? entity.currentVersion);
  const [confirmation, setConfirmation] = useState<'publish' | 'revoke' | null>(null);
  const [pending, setPending] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [message, setMessage] = useState('');
  const request = useRef<AbortController | null>(null);
  const context = useQuery({
    queryKey: ['publication-context', accountId, props.kind, entity.id, entity.version],
    queryFn: async ({
      signal,
    }): Promise<{
      patient: Awaited<ReturnType<typeof patientsApi.getById>>;
      versions: Preview[];
    }> => {
      const patient = await patientsApi.getById(entity.patientId);
      const versions: Preview[] =
        props.kind === 'plan'
          ? (await therapyApi.versions(entity.id, signal)).map((version) => ({
              number: version.versionNumber,
              plan: version.content,
            }))
          : (await recordsApi.history(entity.id, signal)).map((version) => ({
              number: version.versionNumber,
              summary: version.content.patientSummary,
            }));
      return { patient, versions };
    },
    enabled: Boolean(accountId),
    ...therapyOptions,
  });
  const selected = context.data?.versions.find((version) => version.number === number);
  const eligible =
    props.canPublish &&
    context.data?.patient.isLinkedToAccount &&
    !context.data.patient.isArchived &&
    selected &&
    (props.kind === 'plan' || Boolean(selected.summary?.trim()));
  const disabled = props.busy || props.locked || pending || blocked;

  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
    },
    [],
  );

  async function write(action: 'publish' | 'revoke') {
    if (
      request.current ||
      disabled ||
      (action === 'publish' && !eligible) ||
      (action === 'revoke' && !entity.publication)
    )
      return;
    const controller = new AbortController();
    request.current = controller;
    setPending(true);
    props.onBusyChange(true);
    setMessage('');
    setConfirmation(null);
    try {
      if (props.kind === 'plan') {
        const result =
          action === 'publish'
            ? await patientSharingApi.publishPlan(props.entity, number, controller.signal)
            : await patientSharingApi.revokePlan(props.entity, controller.signal);
        if (request.current !== controller) return;
        props.onUpdated(result);
      } else {
        const result =
          action === 'publish'
            ? await patientSharingApi.publishSummary(props.entity, number, controller.signal)
            : await patientSharingApi.revokeSummary(props.entity, controller.signal);
        if (request.current !== controller) return;
        props.onUpdated(result);
      }
      void client.invalidateQueries({ queryKey: ['therapy-plans', accountId, entity.patientId] });
      void client.invalidateQueries({ queryKey: ['medical-records', accountId, entity.patientId] });
      void client.invalidateQueries({
        queryKey: ['appointment-documentation', accountId, entity.patientId],
      });
      void client.invalidateQueries({ queryKey: ['patient', entity.patientId] });
      void client.invalidateQueries({ queryKey: ['therapy-patient', accountId, entity.patientId] });
      toast.success(
        action === 'publish' ? `Udostępniono pacjentowi wersję ${number}.` : 'Udostępnienie cofnięte.',
      );
    } catch (error) {
      if (request.current !== controller) return;
      setMessage(sharingError(error));
      if (needsSharingRefresh(error)) {
        setBlocked(true);
        props.onConflict();
      }
    } finally {
      if (request.current === controller) {
        request.current = null;
        setPending(false);
      }
      props.onBusyChange(false);
    }
  }

  return (
    <section className="min-w-0 space-y-4 rounded-2xl border border-primary/20 bg-card p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-2xl">
            <Share2 size={19} aria-hidden="true" className="text-primary" />
            Dostęp pacjenta
          </h2>
          <p className="mt-2 max-w-3xl text-xs leading-relaxed text-muted-foreground">
            Udostępniasz wybraną zapisaną wersję. Zapis, aktywacja i korekty nie publikują nowych treści
            automatycznie.
          </p>
        </div>
        <PublicationBadge publication={entity.publication} />
      </div>
      {entity.publication && (
        <p className="text-xs leading-relaxed text-muted-foreground">
          Ostatnia publikacja: {recordDate(entity.publication.publishedAt)}.{' '}
          {entity.currentVersion !== entity.publication.versionNumber
            ? `Najnowsza wersja terapeuty to ${entity.currentVersion}; opublikowana treść pozostaje w wersji ${entity.publication.versionNumber}.`
            : 'Opublikowano bieżącą wersję treści.'}{' '}
          Zgodność powiązania konta nie zastępuje sprawdzenia jego aktywności i potwierdzenia e-mail przez
          serwer.
        </p>
      )}
      {context.isPending ? (
        <p role="status" className="text-sm text-muted-foreground">
          Pobieranie wersji i powiązania konta…
        </p>
      ) : context.isError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {sharingError(context.error)}
          </p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={pending || props.busy}
            onClick={() => void context.refetch()}
          >
            Odśwież dane publikacji
          </Button>
        </div>
      ) : (
        <>
          {!context.data.patient.isLinkedToAccount && (
            <p className="rounded-xl bg-muted/50 p-3 text-sm">
              Ta karta nie ma powiązanego konta pacjenta. Nowe udostępnienie nie jest możliwe.
            </p>
          )}
          {context.data.patient.isArchived && (
            <p className="text-sm text-muted-foreground">
              Karta jest archiwalna. Wcześniejsze udostępnienie zostaje zachowane i można je cofnąć, ale nie
              można publikować ponownie.
            </p>
          )}
          {!props.canPublish && (
            <p className="text-sm text-muted-foreground">
              {props.kind === 'plan'
                ? 'Nową publikację umożliwia plan aktywny lub zakończony.'
                : 'Publikacja wymaga zatwierdzonej dokumentacji i zgodnie powiązanej, odbytej wizyty.'}
            </p>
          )}
          {context.data.versions.length > 0 && (
            <label className="block max-w-md space-y-2 text-sm">
              <span>Wersja przeznaczona dla pacjenta</span>
              <select
                className="h-11 w-full rounded-lg border bg-background px-3"
                value={number}
                disabled={disabled}
                onChange={(event) => setNumber(Number(event.target.value))}
              >
                {context.data.versions.map((version) => (
                  <option key={version.number} value={version.number}>
                    Wersja {version.number}
                    {version.number === entity.currentVersion ? ' - najnowsza' : ''}
                    {version.number === entity.publication?.versionNumber ? ' - opublikowana' : ''}
                  </option>
                ))}
              </select>
            </label>
          )}
          {props.kind === 'record' && selected && !selected.summary?.trim() && (
            <p className="text-sm text-muted-foreground">
              Ta zatwierdzona wersja nie ma podsumowania dla pacjenta. Utwórz korektę z podsumowaniem i
              zatwierdź ją przed publikacją.
            </p>
          )}
        </>
      )}
      {props.locked && (
        <p className="text-xs leading-relaxed text-muted-foreground">
          Najpierw zakończ edycję lub rozstrzygnij konflikt. Udostępnianie nie zapisuje formularza i nie
          odrzuca lokalnych zmian.
        </p>
      )}
      {message && (
        <p role="alert" className="text-sm leading-relaxed text-destructive">
          {message}
        </p>
      )}
      {blocked && (
        <Button type="button" variant="outline" disabled={pending || props.busy} onClick={props.onConflict}>
          Pobierz aktualny stan do porównania
        </Button>
      )}
      <div className="flex flex-wrap gap-3">
        <Button
          type="button"
          className="gap-2"
          disabled={disabled || !eligible || context.isError}
          onClick={() => setConfirmation('publish')}
        >
          {pending ? (
            <Loader2 size={16} aria-hidden="true" className="animate-spin motion-reduce:animate-none" />
          ) : (
            <Eye size={16} aria-hidden="true" />
          )}
          Podgląd i udostępnienie
        </Button>
        {entity.publication && (
          <Button
            type="button"
            variant="outline"
            className="gap-2"
            disabled={disabled}
            onClick={() => setConfirmation('revoke')}
          >
            <ShieldOff size={16} aria-hidden="true" />
            Cofnij udostępnienie
          </Button>
        )}
      </div>
      <p className="text-xs leading-relaxed text-muted-foreground">
        {props.kind === 'plan'
          ? 'Cel, zalecenia ogólne i wskazówki w pozycjach planu są przeznaczone dla pacjenta. Nie umieszczaj w nich wewnętrznych notatek.'
          : 'Pacjent otrzyma wyłącznie podsumowanie wybranej wersji, bez wywiadu, oceny funkcjonalnej, wewnętrznych zaleceń i powodów korekt.'}
      </p>
      <Dialog
        open={confirmation !== null}
        onOpenChange={(open) => {
          if (!open && !pending) setConfirmation(null);
        }}
      >
        <DialogContent className="max-h-[90dvh] w-[calc(100%-2rem)] max-w-4xl overflow-y-auto rounded-2xl [&>*]:min-w-0">
          <DialogHeader>
            <DialogTitle>
              {confirmation === 'revoke'
                ? 'Cofnąć udostępnienie?'
                : `Udostępnić pacjentowi wersję ${number}?`}
            </DialogTitle>
            <DialogDescription>
              {confirmation === 'revoke'
                ? 'Kolejne odczyty i wydawanie dostępu do nagrań zostaną zablokowane. Wydane wcześniej linki mogą działać do końca swojej pięciominutowej ważności. Nie można zdalnie usunąć treści już pobranej przez pacjenta.'
                : 'Poniżej treść przeznaczona dla pacjenta. Serwer sprawdzi powiązanie konta, potwierdzenie e-mail i aktywną współpracę.'}
            </DialogDescription>
          </DialogHeader>
          {confirmation === 'publish' &&
            selected &&
            (selected.plan ? (
              <PlanView content={selected.plan} />
            ) : (
              <section className="rounded-xl border p-5">
                <h3 className="mb-3 text-2xl">Podsumowanie dla pacjenta</h3>
                <p className="whitespace-pre-wrap break-words text-sm leading-7">{selected.summary}</p>
              </section>
            ))}
          <div className="flex flex-wrap justify-end gap-3 border-t pt-4">
            <Button type="button" variant="outline" onClick={() => setConfirmation(null)}>
              Anuluj
            </Button>
            <Button
              type="button"
              disabled={disabled || (confirmation === 'publish' && !eligible)}
              onClick={() => {
                if (confirmation) void write(confirmation);
              }}
            >
              {confirmation === 'revoke' ? 'Potwierdzam cofnięcie' : 'Udostępnij tę wersję'}
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </section>
  );
}
