'use client';

import * as Dialog from '@radix-ui/react-dialog';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';

import { Button } from '@/components/ui/button';

export function useUnsavedChanges(dirty: boolean, blocked = false) {
  const router = useRouter();
  const [pending, setPending] = useState<{ run: () => void } | null>(null);

  const confirmLeave = useCallback(
    (run: () => void) => {
      if (blocked) return;

      if (dirty) {
        setPending({ run });
      } else {
        run();
      }
    },
    [dirty, blocked],
  );

  useEffect(() => {
    if (!dirty && !blocked) return;

    function beforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
      event.returnValue = '';
    }

    function handleLink(event: MouseEvent) {
      if (
        event.defaultPrevented ||
        event.button !== 0 ||
        event.ctrlKey ||
        event.metaKey ||
        event.shiftKey ||
        event.altKey
      ) {
        return;
      }

      const element = event.target;

      if (!(element instanceof Element)) return;

      const anchor = element.closest('a[href]');

      if (!(anchor instanceof HTMLAnchorElement)) return;
      if (anchor.hasAttribute('download')) return;
      if (anchor.target && anchor.target !== '_self') return;

      const url = new URL(anchor.href, window.location.href);

      if (url.origin !== window.location.origin) return;

      if (
        url.pathname === window.location.pathname &&
        url.search === window.location.search
      ) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();

      if (blocked) return;

      setPending({
        run: () => router.push(`${url.pathname}${url.search}${url.hash}`),
      });
    }

    window.addEventListener('beforeunload', beforeUnload);
    document.addEventListener('click', handleLink, true);

    return () => {
      window.removeEventListener('beforeunload', beforeUnload);
      document.removeEventListener('click', handleLink, true);
    };
  }, [dirty, blocked, router]);

  const discardDialog = (
    <Dialog.Root
      open={pending !== null}
      onOpenChange={(open) => {
        if (!open) setPending(null);
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[70] bg-black/50" />

        <Dialog.Content className="fixed left-1/2 top-1/2 z-[71] w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl border bg-background p-6 shadow-xl">
          <Dialog.Title className="text-3xl">
            Niezapisane zmiany
          </Dialog.Title>

          <Dialog.Description className="mt-3 text-sm leading-relaxed text-muted-foreground">
            Jeśli opuścisz formularz, wpisane zmiany zostaną utracone.
            Czy chcesz kontynuować?
          </Dialog.Description>

          <div className="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <Button
              type="button"
              variant="outline"
              onClick={() => setPending(null)}
            >
              Zostań w formularzu
            </Button>

            <Button
              type="button"
              variant="destructive"
              onClick={() => {
                const run = pending?.run;
                setPending(null);
                run?.();
              }}
            >
              Odrzuć zmiany
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );

  return { confirmLeave, discardDialog };
}