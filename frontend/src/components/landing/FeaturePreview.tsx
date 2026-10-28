'use client';

import * as Tabs from '@radix-ui/react-tabs';
import { Activity, FileDown, UserRound, X } from 'lucide-react';
import Image from 'next/image';
import { useRef, useState } from 'react';

import patientCard from '../../../public/previews/patient-card.png';
import bodyMap from '../../../public/previews/body-map.png';
import patientPdf from '../../../public/previews/patient-pdf.png';

const previews = [
  {
    id: 'patient',
    label: 'Karta pacjenta',
    icon: UserRound,
    title: 'Najważniejsze informacje w jednym miejscu',
    description:
      'Dane pacjenta, historia medyczna, aktualne leki i notatki w uporządkowanej, czytelnej karcie.',
    image: patientCard,
    alt: 'Przykładowa karta pacjenta w PhysioFlow z danymi i informacjami medycznymi',
    imageClassName: 'max-w-[1000px]',
  },
  {
    id: 'body-map',
    label: 'Mapa bólu',
    icon: Activity,
    title: 'Zobacz dokładnie, gdzie pojawia się ból',
    description:
      'Zaznaczaj lokalizację dolegliwości, określaj ich typ i nasilenie oraz uzupełniaj dokumentację o własne obserwacje.',
    image: bodyMap,
    alt: 'Mapa bólu z sylwetką człowieka, zaznaczonymi punktami i listą dolegliwości',
    imageClassName: 'max-w-[1100px]',
  },
  {
    id: 'pdf',
    label: 'Eksport PDF',
    icon: FileDown,
    title: 'Czytelna karta gotowa do pobrania',
    description:
      'Eksportuj dane pacjenta i informacje medyczne do przejrzystego dokumentu PDF, który możesz zapisać lub wydrukować.',
    image: patientPdf,
    alt: 'Przykładowy eksport karty pacjenta do PDF z informacjami medycznymi',
    imageClassName: 'max-w-[440px]',
  },
];

export default function FeaturePreview() {
  const [activePreview, setActivePreview] = useState('');
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  function closePreview() {
    tabRefs.current[activePreview]?.focus();
    setActivePreview('');
  }

  return (
    <section id="preview" className="border-t px-6 py-20 md:py-24">
      <div className="mx-auto max-w-6xl">
        <div className="mb-8 text-center">
          <span className="mb-3 inline-block text-xs font-semibold uppercase tracking-[0.18em] text-primary">
            Poznaj aplikację
          </span>

          <h2 className="text-3xl font-bold md:text-4xl">
            Zobacz PhysioFlow w praktyce
          </h2>
        </div>

        <Tabs.Root
          value={activePreview}
          onValueChange={setActivePreview}
          activationMode="manual"
        >
          <Tabs.List
            aria-label="Podgląd funkcji aplikacji"
            className="mx-auto grid max-w-xl grid-cols-3 gap-1 rounded-xl border bg-muted/40 p-1.5"
          >
            {previews.map((preview) => {
              const Icon = preview.icon;

              return (
                <Tabs.Trigger
                  key={preview.id}
                  ref={(element) => {
                    tabRefs.current[preview.id] = element;
                  }}
                  value={preview.id}
                  className="flex min-h-12 flex-col items-center justify-center gap-1 rounded-lg px-2 py-2 text-xs text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 data-[state=active]:bg-background data-[state=active]:text-primary data-[state=active]:shadow-sm sm:flex-row sm:gap-2 sm:text-sm"
                >
                  <Icon aria-hidden="true" className="h-4 w-4 shrink-0" />
                  {preview.label}
                </Tabs.Trigger>
              );
            })}
          </Tabs.List>

          {activePreview && (
            <div className="mt-4 flex justify-center">
              <button
                type="button"
                onClick={closePreview}
                className="inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
              >
                <X aria-hidden="true" className="h-4 w-4" />
                Zamknij podgląd
              </button>
            </div>
          )}

          {previews.map((preview) => (
            <Tabs.Content
              key={preview.id}
              value={preview.id}
              className="mt-8 rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-4"
            >
              <div className="mb-6 text-center">
                <h3 className="text-2xl md:text-3xl">{preview.title}</h3>

                <p className="mx-auto mt-3 max-w-2xl text-sm leading-relaxed text-muted-foreground md:text-base">
                  {preview.description}
                </p>
              </div>

              <div className="overflow-hidden rounded-2xl border bg-card shadow-xl shadow-primary/5">
                <div className="flex items-center justify-between gap-3 border-b bg-muted/30 px-4 py-3">
                  <span className="text-sm font-semibold">PhysioFlow</span>

                  <span className="rounded-full border border-primary/15 bg-primary/5 px-3 py-1 text-xs text-primary">
                    {preview.label}
                  </span>
                </div>

                <div className="flex h-[340px] items-center justify-center bg-gradient-to-br from-primary/5 via-background to-muted/40 p-3 sm:h-[480px] sm:p-6 lg:h-[680px]">
                  <Image
                    src={preview.image}
                    alt={preview.alt}
                    sizes={
                      preview.id === 'pdf'
                        ? '(max-width: 640px) 85vw, 440px'
                        : '(max-width: 1200px) 90vw, 1100px'
                    }
                    className={`block h-auto max-h-full w-auto max-w-full rounded-md object-contain shadow-sm ${preview.imageClassName}`}
                  />
                </div>
              </div>

              <p className="mt-4 text-center text-xs text-muted-foreground">
                Podgląd demonstracyjny aplikacji.
              </p>
            </Tabs.Content>
          ))}
        </Tabs.Root>
      </div>
    </section>
  );
}