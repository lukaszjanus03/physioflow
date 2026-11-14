'use client';

import {
  Activity,
  ClipboardList,
  FileText,
  HeartPulse,
  ListChecks,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';

import { Button } from '@/components/ui/button';
import {
  recordError,
  recordFields,
} from '@/lib/medical-records-api';
import type {
  ContentKey,
  RecordContent,
} from '@/lib/medical-records-api';

const fieldIcons: Record<ContentKey, LucideIcon> = {
  interview: ClipboardList,
  functionalAssessment: Activity,
  performedActions: HeartPulse,
  recommendations: ListChecks,
  patientSummary: FileText,
};

export function RecordFailure({
  error,
  retry,
}: {
  error: unknown;
  retry?: () => void;
}) {
  return (
    <div className="space-y-3 rounded-xl border border-destructive/30 bg-card p-5">
      <p
        role="alert"
        className="text-sm text-destructive"
      >
        {recordError(error)}
      </p>

      {retry && (
        <Button
          type="button"
          variant="outline"
          onClick={retry}
        >
          Spróbuj ponownie
        </Button>
      )}
    </div>
  );
}

export function RecordContentView({
  content,
}: {
  content: RecordContent;
}) {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {recordFields.map((field) => {
        const Icon = fieldIcons[field.key];
        const summary =
          field.key === 'patientSummary';
        const text = content[field.key];

        return (
          <section
            key={field.key}
            className={`min-w-0 overflow-hidden rounded-2xl border bg-card ${
              summary
                ? 'border-primary/25 lg:col-span-2'
                : ''
            }`}
          >
            <header
              className={`flex items-center gap-3 border-b px-5 py-4 ${
                summary
                  ? 'bg-primary/5'
                  : 'bg-muted/20'
              }`}
            >
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
                <Icon
                  aria-hidden="true"
                  className="h-5 w-5"
                  strokeWidth={1.7}
                />
              </span>

              <h3 className="text-2xl leading-tight">
                {field.label}
              </h3>
            </header>

            <div className="p-5">
              {text?.trim() ? (
                <p className="whitespace-pre-wrap break-words text-sm leading-7">
                  {text}
                </p>
              ) : (
                <p className="text-sm italic text-muted-foreground">
                  Nie uzupełniono.
                </p>
              )}

              {summary && (
                <p className="mt-4 border-t border-primary/15 pt-3 text-xs leading-relaxed text-muted-foreground">
                  Osobne podsumowanie przeznaczone
                  dla pacjenta. W obecnym etapie
                  nie jest publikowane w portalu.
                </p>
              )}
            </div>
          </section>
        );
      })}
    </div>
  );
}