'use client';

import { useCallback, useState } from 'react';
import { JournalAction, JournalPlanHistory } from '@/components/patient-journal/JournalAction';
import { useQuery } from '@tanstack/react-query';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  ArrowLeft,
  ArrowUpRight,
  CalendarDays,
  BookOpen,
  Clock3,
  Layers3,
  UserRound,
  ClipboardList,
  FileText,
  RefreshCw,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { LoadingState } from '@/components/ui/loading-state';
import ExerciseVideoPlayer from '@/components/therapy/ExerciseVideoPlayer';
import { Pages } from '@/components/therapy/TherapyUI';
import { usePatientContent } from '@/hooks/use-patient-content';
import { patientSharingApi, sharingError } from '@/lib/patient-sharing-api';
import type { PatientPlanListItem, PatientSummaryListItem } from '@/lib/patient-sharing-api';
import { cooperationApi, cooperationKeys } from '@/lib/cooperation-api';
import { recordDate } from '@/lib/medical-records-api';
import { therapyDate, therapyId } from '@/lib/therapy-api';
import type { PaginatedList } from '@/types/api';

type Kind = 'plans' | 'summaries';
export default function PatientSharedContent({ kind, accountId }: { kind: Kind; accountId: string }) {
  const router = useRouter();
  const params = useSearchParams();
  const id = params.get('content');
  const rawPage = Number(params.get('page') ?? 1);
  const page = Number.isSafeInteger(rawPage) && rawPage > 0 ? rawPage : 1;
  const therapists = useQuery({
    queryKey: cooperationKeys.therapists(accountId),
    queryFn: ({ signal }) => cooperationApi.therapists(signal),
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });
  const therapistName = (therapistId: string) => {
    const match = therapists.data?.find((item) => item.therapist.id === therapistId)?.therapist;
    return match ? `${match.firstName} ${match.lastName}` : 'Terapeuta prowadzący';
  };
  function navigate(content: string | null, nextPage = page) {
    const next = new URLSearchParams({ view: kind, page: String(nextPage) });
    if (content) next.set('content', content);
    router.push(`/portal?${next.toString()}`, { scroll: true });
  }
  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4 rounded-2xl border bg-card p-6 sm:p-8">
        <div>
          <p className="mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-primary">
            {kind === 'plans' ? (
              <ClipboardList size={17} aria-hidden="true" />
            ) : (
              <FileText size={17} aria-hidden="true" />
            )}
            Udostępnione przez terapeutę
          </p>
          <h1 className="text-4xl">{kind === 'plans' ? 'Moje plany' : 'Podsumowania wizyt'}</h1>
          <p className="mt-3 max-w-2xl text-sm leading-relaxed text-muted-foreground">
            {kind === 'plans'
              ? 'Zapisane plany, instrukcje ćwiczeń i materiały wybrane dla Ciebie przez terapeutę.'
              : 'Podsumowania wizyt, które terapeuta świadomie udostępnił Twojemu kontu.'}
          </p>
        </div>
        {id && (
          <Button type="button" variant="outline" className="gap-2" onClick={() => navigate(null)}>
            <ArrowLeft size={16} aria-hidden="true" />
            Wróć do listy
          </Button>
        )}
      </header>
      {id ? (
        !therapyId(id) ? (
          <p role="alert" className="text-sm text-destructive">
            Nieprawidłowy adres treści.
          </p>
        ) : kind === 'plans' ? (
          <PlanDetails key={`${accountId}:${id}`} id={id} therapistName={therapistName} />
        ) : (
          <SummaryDetails key={`${accountId}:${id}`} id={id} therapistName={therapistName} />
        )
      ) : (
        <ContentList
          key={`${accountId}:${kind}:${page}`}
          kind={kind}
          page={page}
          open={(id) => navigate(id)}
          changePage={(next) => navigate(null, next)}
          therapistName={therapistName}
        />
      )}
    </div>
  );
}

function Failure({ error, retry }: { error: unknown; retry: () => void }) {
  return (
    <section className="space-y-4 rounded-2xl border bg-card p-6">
      <p role="alert" className="text-sm leading-relaxed text-destructive">
        {sharingError(error)}
      </p>
      <Button type="button" variant="outline" onClick={retry}>
        Pobierz aktualne dane
      </Button>
    </section>
  );
}

function ContentList({
  kind,
  page,
  open,
  changePage,
  therapistName,
}: {
  kind: Kind;
  page: number;
  open: (id: string) => void;
  changePage: (page: number) => void;
  therapistName: (id: string) => string;
}) {
  const load = useCallback(
    (signal: AbortSignal): Promise<PaginatedList<PatientPlanListItem | PatientSummaryListItem>> =>
      kind === 'plans' ? patientSharingApi.plans(page, signal) : patientSharingApi.summaries(page, signal),
    [kind, page],
  );
  const query = usePatientContent(load);
  if (query.loading) return <LoadingState title="Pobieranie udostępnionych treści…" />;
  if (query.error) return <Failure error={query.error} retry={query.reload} />;
  if (!query.data) return null;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">Udostępnione: {query.data.totalCount}</p>
        <Button type="button" variant="outline" className="gap-2" onClick={query.reload}>
          <RefreshCw size={15} aria-hidden="true" />
          Odśwież
        </Button>
      </div>
      {!query.data.items.length && (
        <section className="space-y-3 rounded-2xl border bg-card p-8 text-center">
          <h2 className="text-3xl">
            {page > 1
              ? 'Brak treści na tej stronie'
              : kind === 'plans'
                ? 'Nie masz jeszcze udostępnionych planów'
                : 'Nie masz jeszcze udostępnionych podsumowań'}
          </h2>
          <p className="text-sm leading-relaxed text-muted-foreground">
            {page > 1
              ? 'Wróć do poprzedniej strony wyników.'
              : 'Treść pojawi się tutaj po udostępnieniu przez terapeutę. Samo utworzenie planu lub odbycie wizyty nie publikuje jej automatycznie.'}
          </p>
        </section>
      )}
      <div className="grid items-start gap-4 xl:grid-cols-2">
        {query.data.items.map((item) => (
          <article key={item.id} className="min-w-0 space-y-4 rounded-2xl border bg-card p-6">
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span className="rounded-full bg-primary/10 px-3 py-1 text-primary">
                Wersja {item.versionNumber}
              </span>
              <span>{therapistName(item.therapistId)}</span>
            </div>
            <h2 className="break-words text-3xl">
              {'name' in item
                ? item.name || 'Plan terapii'
                : `Wizyta: ${recordDate(item.appointmentStartsAt)}`}
            </h2>
            {'name' in item && (
              <p className="flex items-center gap-2 text-sm text-muted-foreground">
                <CalendarDays size={16} aria-hidden="true" />
                {therapyDate(item.startDate)} – {therapyDate(item.endDate)}
              </p>
            )}
            <p className="text-xs text-muted-foreground">Udostępniono: {recordDate(item.publishedAt)}</p>
            <Button type="button" variant="outline" className="gap-2" onClick={() => open(item.id)}>
              {'name' in item ? 'Zobacz plan' : 'Przeczytaj podsumowanie'}
              <ArrowUpRight size={16} aria-hidden="true" />
            </Button>
          </article>
        ))}
      </div>
      {(query.data.totalPages > 1 || page > 1) && (
        <Pages
          page={page}
          pages={query.data.totalPages}
          next={query.data.hasNext}
          busy={false}
          onChange={changePage}
        />
      )}
    </div>
  );
}

function PlanDetails({ id, therapistName }: { id: string; therapistName: (id: string) => string }) {
  const [journalOpen, setJournalOpen] = useState(false);
  const load = useCallback((signal: AbortSignal) => patientSharingApi.plan(id, signal), [id]);
  const query = usePatientContent(load, journalOpen);
  if (query.loading) return <LoadingState title="Pobieranie planu…" />;
  if (query.error) return <Failure error={query.error} retry={query.reload} />;
  const plan = query.data;
  if (!plan) return null;
  return (
    <div className="space-y-5">
      <section className="space-y-4 rounded-2xl border bg-card p-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground">
            {therapistName(plan.therapistId)} · udostępniona wersja {plan.versionNumber}
          </p>
          <Button type="button" variant="outline" size="sm" disabled={journalOpen} onClick={query.reload}>
            Odśwież plan
          </Button>
        </div>
        <h2 className="break-words text-3xl">{plan.content.name || 'Plan terapii'}</h2>
        <JournalPlanHistory planId={plan.id} />
        <p className="text-sm text-muted-foreground">
          {therapyDate(plan.content.startDate)} – {therapyDate(plan.content.endDate)}
        </p>
        {plan.content.goal && (
          <div>
            <h3 className="mb-2 text-xl">Cel terapii</h3>
            <p className="whitespace-pre-wrap break-words text-sm leading-7">{plan.content.goal}</p>
          </div>
        )}
        {plan.content.generalRecommendations && (
          <div>
            <h3 className="mb-2 text-xl">Zalecenia ogólne</h3>
            <p className="whitespace-pre-wrap break-words text-sm leading-7">
              {plan.content.generalRecommendations}
            </p>
          </div>
        )}
        <p className="text-xs text-muted-foreground">
          Udostępniono: {recordDate(plan.publishedAt)}. To treść wersji wybranej przez terapeutę.
        </p>
      </section>
      <div className="grid items-start gap-4 xl:grid-cols-2">
        {[...plan.content.exercises]
          .sort((a, b) => a.order - b.order)
          .map((item, index) => (
            <article key={item.id} className="min-w-0 space-y-4 rounded-2xl border bg-card p-6">
              <h3 className="break-words text-2xl">
                {index + 1}. {item.name}
              </h3>
              <p className="whitespace-pre-wrap break-words text-sm leading-7">
                {item.instructions || 'Nie dodano instrukcji.'}
              </p>
              <div className="flex flex-wrap gap-2 text-sm">
                <span className="rounded-lg bg-primary/10 px-3 py-2">Serie: {item.sets ?? '—'}</span>
                {item.repetitions !== null && (
                  <span className="rounded-lg bg-muted px-3 py-2">Powtórzenia: {item.repetitions}</span>
                )}
                {item.durationSeconds !== null && (
                  <span className="rounded-lg bg-muted px-3 py-2">Czas serii: {item.durationSeconds} s</span>
                )}
              </div>
              <p className="whitespace-pre-wrap break-words text-sm">
                <strong>Częstotliwość:</strong> {item.frequency || 'Nie określono.'}
              </p>
              {item.notes && (
                <div className="rounded-xl bg-muted/40 p-4">
                  <p className="mb-2 text-xs font-semibold text-muted-foreground">Wskazówki do ćwiczenia</p>
                  <p className="whitespace-pre-wrap break-words text-sm leading-7">{item.notes}</p>
                </div>
              )}
              <div className="border-t pt-4">
                <JournalAction
                  onEditingChange={setJournalOpen}
                  target={{
                    therapyPlanId: plan.id,
                    planVersionNumber: plan.versionNumber,
                    planItemId: item.id,
                    publishedAt: plan.publishedAt,
                    planName: plan.content.name,
                    exercise: {
                      name: item.name,
                      instructions: item.instructions,
                      order: item.order,
                      sets: item.sets,
                      repetitions: item.repetitions,
                      durationSeconds: item.durationSeconds,
                      frequency: item.frequency,
                      notes: item.notes,
                    },
                  }}
                />
              </div>
              {item.video && (
                <ExerciseVideoPlayer
                  video={{ id: item.id, ...item.video, contentType: null, sizeBytes: null }}
                  title={item.name}
                  patientScope={{
                    planId: plan.id,
                    itemId: item.id,
                    versionNumber: plan.versionNumber,
                    onAccessLost: query.loseAccess,
                  }}
                />
              )}
            </article>
          ))}
      </div>
    </div>
  );
}

function SummaryDetails({ id, therapistName }: { id: string; therapistName: (id: string) => string }) {
  const load = useCallback((signal: AbortSignal) => patientSharingApi.summary(id, signal), [id]);
  const query = usePatientContent(load);
  if (query.loading) return <LoadingState title="Pobieranie podsumowania…" />;
  if (query.error) return <Failure error={query.error} retry={query.reload} />;
  const summary = query.data;
  if (!summary) return null;

  const information = [
    { label: 'Termin wizyty', value: recordDate(summary.appointmentStartsAt), icon: CalendarDays },
    { label: 'Terapeuta', value: therapistName(summary.therapistId), icon: UserRound },
    { label: 'Udostępniona wersja', value: `Wersja ${summary.versionNumber}`, icon: Layers3 },
    { label: 'Data udostępnienia', value: recordDate(summary.publishedAt), icon: Clock3 },
  ];
  const paragraphs = summary.summary.split(/\r?\n\s*\r?\n/).filter((paragraph) => paragraph.trim());
  const contentHeading = `summary-text-${summary.id}`;

  return (
    <article className="min-w-0 overflow-hidden rounded-2xl border bg-card shadow-sm">
      <header className="flex flex-wrap items-start justify-between gap-4 border-b p-5 sm:p-7">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <FileText size={23} strokeWidth={1.7} aria-hidden="true" />
          </span>
          <div className="min-w-0">
            <p className="mb-1 text-xs font-semibold uppercase tracking-widest text-primary">
              Dokument dla pacjenta
            </p>
            <h2 className="text-3xl">Podsumowanie wizyty</h2>
          </div>
        </div>
        <Button type="button" variant="outline" size="sm" className="gap-2" onClick={query.reload}>
          <RefreshCw size={15} aria-hidden="true" />
          Odśwież podsumowanie
        </Button>
      </header>

      <div className="border-b bg-muted/15 p-4 sm:p-6">
        <dl className="grid grid-cols-2 gap-3 xl:grid-cols-4">
          {information.map(({ label, value, icon: Icon }) => (
            <div key={label} className="min-w-0 rounded-xl border bg-card p-3.5 sm:p-4">
              <dt className="flex items-start gap-2 text-xs leading-relaxed text-muted-foreground">
                <Icon size={15} aria-hidden="true" className="mt-0.5 shrink-0 text-primary" />
                <span>{label}</span>
              </dt>
              <dd className="mt-3 text-sm font-semibold leading-6 [overflow-wrap:anywhere]">{value}</dd>
            </div>
          ))}
        </dl>
      </div>

      <section aria-labelledby={contentHeading} className="p-5 sm:p-7">
        <div className="overflow-hidden rounded-xl border">
          <header className="flex items-center gap-3 border-b bg-primary/5 px-5 py-4">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <BookOpen size={18} strokeWidth={1.7} aria-hidden="true" />
            </span>
            <h3 id={contentHeading} className="text-2xl">
              Podsumowanie dla Ciebie
            </h3>
          </header>
          <div className="max-w-4xl space-y-4 p-5 sm:p-6">
            {paragraphs.length ? (
              paragraphs.map((paragraph, index) => (
                <p key={index} className="whitespace-pre-wrap text-sm leading-7 [overflow-wrap:anywhere]">
                  {paragraph}
                </p>
              ))
            ) : (
              <p className="text-sm text-muted-foreground">Podsumowanie nie zawiera treści.</p>
            )}
          </div>
        </div>
      </section>
    </article>
  );
}
