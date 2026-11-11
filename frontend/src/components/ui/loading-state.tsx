import { Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';

type LoadingStateProps = {
  title?: string;
  description?: string;
  className?: string;
};

export function LoadingState({
  title = 'Wczytywanie…',
  description,
  className,
}: LoadingStateProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      className={cn(
        'flex min-h-40 flex-col items-center justify-center gap-4 rounded-xl border bg-card p-6 text-center',
        className,
      )}
    >
      <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10">
        <Loader2
          aria-hidden="true"
          className="h-7 w-7 animate-spin text-primary motion-reduce:animate-none"
        />
      </div>

      <div className="space-y-1">
        <p className="text-sm font-semibold">{title}</p>

        {description && (
          <p className="text-sm text-muted-foreground">
            {description}
          </p>
        )}
      </div>
    </div>
  );
}