'use client';

import {
  Activity,
  ArrowRight,
  CalendarCheck,
  ChevronDown,
  ClipboardList,
  Code2,
  Database,
  Github,
  HeartPulse,
  Languages,
  Layers,
  LineChart,
  Lock,
  ShieldCheck,
  Sparkles,
  Stethoscope,
  Users,
  Zap,
} from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/stores/auth-store';

export default function HomePage() {
  const router = useRouter();
  const { isAuthenticated, roles } = useAuthStore();
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    if (!isAuthenticated) return;
    if (roles.includes('Therapist')) router.replace('/dashboard');
    else if (roles.includes('Patient')) router.replace('/portal');
  }, [isAuthenticated, roles, router]);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 20);
    window.addEventListener('scroll', onScroll);
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <div className="relative min-h-screen overflow-x-hidden bg-background">
      <div
        aria-hidden
        className="pointer-events-none fixed inset-0 -z-10 opacity-50"
        style={{
          backgroundImage: `
            radial-gradient(at 20% 20%, hsl(173 80% 50% / 0.35) 0px, transparent 50%),
            radial-gradient(at 80% 0%, hsl(160 84% 50% / 0.30) 0px, transparent 50%),
            radial-gradient(at 70% 70%, hsl(189 94% 55% / 0.25) 0px, transparent 50%)
          `,
        }}
      />

      <header
        className={`sticky top-0 z-40 transition-all ${
          scrolled ? 'border-b bg-background/80 backdrop-blur-lg' : 'bg-transparent'
        }`}
      >
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <Link href="/" className="flex items-center gap-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-primary to-emerald-500 shadow-lg shadow-primary/30">
              <HeartPulse className="h-4 w-4 text-white" />
            </div>
            <span className="text-lg font-bold">PhysioFlow</span>
          </Link>

          <div className="hidden items-center gap-1 text-sm md:flex">
            <a href="#features" className="rounded-md px-3 py-2 text-muted-foreground transition-colors hover:text-foreground">Funkcje</a>
            <a href="#how" className="rounded-md px-3 py-2 text-muted-foreground transition-colors hover:text-foreground">Jak to działa</a>
            <a href="#tech" className="rounded-md px-3 py-2 text-muted-foreground transition-colors hover:text-foreground">Technologia</a>
            <a href="#faq" className="rounded-md px-3 py-2 text-muted-foreground transition-colors hover:text-foreground">FAQ</a>
          </div>

          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" asChild>
              <Link href="/login">Zaloguj</Link>
            </Button>
            <Button size="sm" asChild>
              <Link href="/register">
                Załóż konto
                <ArrowRight className="ml-1 h-3.5 w-3.5" />
              </Link>
            </Button>
          </div>
        </div>
      </header>

      <main>
        <section className="relative px-6 pb-24 pt-20">
          <div className="mx-auto grid max-w-6xl items-center gap-10 lg:grid-cols-[1.05fr_1fr]">
            <div className="space-y-6">
              <div className="inline-flex items-center gap-2 rounded-full border border-primary/30 bg-primary/5 px-3 py-1 text-xs font-medium">
                <Sparkles className="h-3.5 w-3.5 text-primary" />
              </div>

              <h1 className="text-4xl font-bold leading-[1.05] tracking-tight md:text-5xl lg:text-6xl">
                Cyfrowy ekosystem dla
                <span className="block bg-gradient-to-r from-primary via-emerald-500 to-cyan-500 bg-clip-text text-transparent">
                  fizjoterapii
                </span>
                bez papierologii
              </h1>

              <p className="max-w-lg text-lg text-muted-foreground">
                Karty pacjentów, kalendarz wizyt, plany ćwiczeń, mapa bólu i komunikacja z pacjentem - wszystko w jednym miejscu, dostępne z przeglądarki.
              </p>

              <div className="flex flex-wrap gap-3">
                <Button size="lg" asChild className="group">
                  <Link href="/register">
                    Zacznij teraz
                    <ArrowRight className="ml-1 h-4 w-4 transition-transform group-hover:translate-x-0.5" />
                  </Link>
                </Button>
                <Button size="lg" variant="outline" asChild>
                  <Link href="/login">Zaloguj się</Link>
                </Button>
              </div>

              <div className="flex flex-wrap items-center gap-x-6 gap-y-2 pt-2 text-xs text-muted-foreground">
                <span className="flex items-center gap-1.5"><Lock className="h-3.5 w-3.5" /> Zgodne z RODO</span>
                <span className="flex items-center gap-1.5"><ShieldCheck className="h-3.5 w-3.5" /> JWT + role</span>
                <span className="flex items-center gap-1.5"><Code2 className="h-3.5 w-3.5" /> Open source</span>
              </div>
            </div>

            <div className="relative">
              <div
                aria-hidden
                className="absolute -inset-8 -z-10 rounded-[40px] bg-gradient-to-br from-primary/25 via-emerald-500/20 to-cyan-500/25 opacity-70 blur-3xl"
              />
              <div className="overflow-hidden rounded-2xl border bg-card shadow-2xl shadow-primary/10 ring-1 ring-zinc-900/5">
                <img
                  src="/preview-laptop.jpg"
                  alt="PhysioFlow — panel terapeuty na biurku w gabinecie fizjoterapii"
                  className="block h-auto w-full"
                />
              </div>
            </div>
          </div>
        </section>

        <section id="how" className="border-y bg-card/30 px-6 py-20 backdrop-blur-sm">
          <div className="mx-auto max-w-6xl">
            <div className="mb-12 text-center">
              <h2 className="text-3xl font-bold md:text-4xl">Jak to działa</h2>
              <p className="mt-3 text-muted-foreground">Od rejestracji terapeuty do działającego workflow w 4 krokach</p>
            </div>
            <div className="grid gap-6 md:grid-cols-4">
              {[
                { num: '01', icon: <Stethoscope className="h-5 w-5" />, title: 'Załóż konto', desc: 'Zarejestruj się jako fizjoterapeuta. Zajmie minutę.' },
                { num: '02', icon: <Users className="h-5 w-5" />, title: 'Dodaj pacjentów', desc: 'Wprowadź dane osobowe, historię medyczną, leki i notatki.' },
                { num: '03', icon: <CalendarCheck className="h-5 w-5" />, title: 'Zaplanuj wizyty', desc: 'Kalendarz z drag-and-drop, automatyczne przypomnienia.' },
                { num: '04', icon: <LineChart className="h-5 w-5" />, title: 'Śledź postępy', desc: 'Mapa bólu, dziennik pacjenta, plany ćwiczeń. Wszystko w jednym.' },
              ].map((step) => (
                <div key={step.num} className="rounded-xl border bg-card p-5 transition-all hover:-translate-y-1 hover:shadow-lg hover:shadow-primary/10">
                  <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-gradient-to-br from-primary to-emerald-500 text-white shadow-lg shadow-primary/30">
                    {step.icon}
                  </div>
                  <div className="mb-1 font-mono text-xs font-bold text-primary">{step.num}</div>
                  <h3 className="mb-1.5 font-semibold">{step.title}</h3>
                  <p className="text-sm text-muted-foreground">{step.desc}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section id="features" className="px-6 py-24">
          <div className="mx-auto max-w-6xl">
            <div className="mb-12 text-center">
              <h2 className="text-3xl font-bold md:text-4xl">Wszystko czego potrzebujesz</h2>
              <p className="mt-3 text-muted-foreground">Sześć modułów pokrywających typowy workflow gabinetu</p>
            </div>
            <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {[
                { icon: <ClipboardList />, title: 'Karty pacjentów', desc: 'PESEL, dane medyczne, historia, leki — w jednej karcie z walidacją.', color: 'from-teal-500 to-cyan-500' },
                { icon: <CalendarCheck />, title: 'Kalendarz wizyt', desc: 'Tygodniowy widok, drag-and-drop, dostępność godzinowa.', color: 'from-emerald-500 to-teal-500' },
                { icon: <Activity />, title: 'Mapa bólu', desc: 'Zaznacz punkty na sylwetce ciała i śledź ich zmianę w czasie.', color: 'from-rose-400 to-orange-500' },
                { icon: <LineChart />, title: 'Plany terapeutyczne', desc: 'Biblioteka ćwiczeń i indywidualne plany dla każdego pacjenta.', color: 'from-green-500 to-emerald-500' },
                { icon: <HeartPulse />, title: 'Portal pacjenta', desc: 'Pacjent ćwiczy w domu, prowadzi dziennik bólu i komunikuje się z Tobą.', color: 'from-cyan-500 to-sky-500' },
                { icon: <ShieldCheck />, title: 'Bezpieczeństwo', desc: 'JWT, ASP.NET Identity, role, szyfrowanie i zgodność z RODO.', color: 'from-teal-600 to-emerald-600' },
              ].map((f) => (
                <div key={f.title} className="group relative overflow-hidden rounded-xl border bg-card p-6 transition-all hover:-translate-y-1 hover:shadow-lg">
                  <div className={`absolute inset-x-0 top-0 h-1 bg-gradient-to-r ${f.color} opacity-0 transition-opacity group-hover:opacity-100`} />
                  <div className={`mb-4 inline-flex h-10 w-10 items-center justify-center rounded-lg bg-gradient-to-br ${f.color} text-white shadow-md`}>
                    {f.icon}
                  </div>
                  <h3 className="mb-2 font-semibold">{f.title}</h3>
                  <p className="text-sm text-muted-foreground">{f.desc}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section id="tech" className="border-y bg-card/30 px-6 py-24 backdrop-blur-sm">
          <div className="mx-auto max-w-6xl">
            <div className="mb-12 text-center">
              <div className="mb-3 inline-flex items-center gap-1.5 rounded-full border bg-card px-3 py-1 text-xs font-medium text-muted-foreground">
                <Code2 className="h-3 w-3" />
                Pod maską
              </div>
              <h2 className="text-3xl font-bold md:text-4xl">Nowoczesny stack technologiczny</h2>
              <p className="mt-3 text-muted-foreground">Wybór oparty o produkcyjne best-practices, nie modę</p>
            </div>
            <div className="grid gap-6 md:grid-cols-3">
              <div className="rounded-xl border bg-card p-6">
                <div className="mb-4 flex items-center gap-2 font-semibold">
                  <Layers className="h-4 w-4 text-primary" />
                  Frontend
                </div>
                <ul className="space-y-2 text-sm text-muted-foreground">
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Next.js 15 (App Router)</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> TypeScript + React 19</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Tailwind CSS + shadcn/ui</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> TanStack Query + Zustand</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> React Hook Form + Zod</li>
                </ul>
              </div>
              <div className="rounded-xl border bg-card p-6">
                <div className="mb-4 flex items-center gap-2 font-semibold">
                  <Zap className="h-4 w-4 text-primary" />
                  Backend
                </div>
                <ul className="space-y-2 text-sm text-muted-foreground">
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> .NET 8 Web API</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Entity Framework Core</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> CQRS z MediatR</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> ASP.NET Identity + JWT</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> SignalR (real-time)</li>
                </ul>
              </div>
              <div className="rounded-xl border bg-card p-6">
                <div className="mb-4 flex items-center gap-2 font-semibold">
                  <Database className="h-4 w-4 text-primary" />
                  Infrastruktura
                </div>
                <ul className="space-y-2 text-sm text-muted-foreground">
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> PostgreSQL 16</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Docker + Docker Compose</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Cloudflare R2 (storage)</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> Resend (e-mail)</li>
                  <li className="flex items-center gap-2"><span className="h-1.5 w-1.5 rounded-full bg-primary" /> GitHub Actions (CI/CD)</li>
                </ul>
              </div>
            </div>
          </div>
        </section>

        <section id="faq" className="px-6 py-24">
          <div className="mx-auto max-w-3xl">
            <div className="mb-12 text-center">
              <h2 className="text-3xl font-bold md:text-4xl">Pytania i odpowiedzi</h2>
              <p className="mt-3 text-muted-foreground">Najczęściej zadawane pytania o aplikację</p>
            </div>
            <div className="space-y-3">
              {[
                {
                  q: 'Czy aplikacja jest zgodna z RODO?',
                  a: 'Tak. Dane pacjentów są szyfrowane w tranzycie (HTTPS), a dostęp ograniczony do zalogowanego terapeuty (JWT + role). W warstwie aplikacyjnej każdy pacjent jest powiązany z konkretnym terapeutą — terapeuci nie widzą wzajemnie swoich pacjentów.',
                },
                {
                  q: 'Czy kod aplikacji jest dostępny publicznie?',
                  a: 'Tak — projekt powstał jako praca inżynierska i jest open source. Repo na GitHubie zawiera frontend, backend, dokumentację techniczną oraz ADR (Architecture Decision Records).',
                },
                {
                  q: 'Czy mogę uruchomić aplikację u siebie (self-hosted)?',
                  a: 'Tak. Wystarczy Docker, Node.js 20 i .NET 8 SDK. Pełen tutorial znajdziesz w README repozytorium — startujemy w trzech komendach.',
                },
                {
                  q: 'Czy można eksportować dane pacjenta?',
                  a: 'Tak — przewidujemy eksport karty pacjenta do PDF (z @react-pdf/renderer). Dodatkowo, ponieważ baza to PostgreSQL, można w każdej chwili zrobić zrzut danych przez pg_dump.',
                },
                {
                  q: 'Jakie są dalsze plany rozwoju?',
                  a: 'Mapa bólu z react-konva, plany terapeutyczne z biblioteką ćwiczeń, czat z pacjentem przez SignalR, automatyczne przypomnienia o wizytach przez e-mail (Resend). Roadmapa jest w docs/roadmap.md.',
                },
              ].map((item) => (
                <details
                  key={item.q}
                  className="group rounded-xl border bg-card transition-colors hover:bg-accent/30"
                >
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-4 p-5 font-medium [&::-webkit-details-marker]:hidden">
                    {item.q}
                    <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" />
                  </summary>
                  <div className="border-t p-5 pt-4 text-sm text-muted-foreground">
                    {item.a}
                  </div>
                </details>
              ))}
            </div>
          </div>
        </section>

        <section className="border-y bg-card/30 px-6 py-24 backdrop-blur-sm">
          <div className="mx-auto max-w-4xl">
            <div className="mb-12 text-center">
              <h2 className="text-3xl font-bold md:text-4xl">Autorzy</h2>
            </div>
            <div className="grid gap-5 sm:grid-cols-2">
              {[
                { initials: 'HR', name: 'Hubert Rola', role: 'Frontend & UX' },
                { initials: 'ŁJ', name: 'Łukasz Janus', role: 'Backend & Infrastructure' },
              ].map((p) => (
                <div key={p.name} className="rounded-xl border bg-card p-6 text-center transition-all hover:-translate-y-1 hover:shadow-lg">
                  <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-gradient-to-br from-primary to-emerald-500 text-xl font-bold text-white shadow-lg shadow-primary/30">
                    {p.initials}
                  </div>
                  <h3 className="font-semibold">{p.name}</h3>
                  <p className="mt-1 text-sm text-muted-foreground">{p.role}</p>
                </div>
              ))}
            </div>
          </div>
        </section>

        <section className="px-6 py-24">
          <div className="mx-auto max-w-4xl">
            <div className="relative overflow-hidden rounded-2xl border bg-gradient-to-br from-primary/10 via-emerald-500/10 to-cyan-500/10 p-12 text-center">
              <div
                aria-hidden
                className="pointer-events-none absolute inset-0 -z-10 opacity-50"
                style={{
                  backgroundImage: 'radial-gradient(at 50% 0%, hsl(173 80% 50% / 0.3) 0px, transparent 70%)',
                }}
              />
              <h2 className="text-3xl font-bold md:text-4xl">Gotowy, by zacząć?</h2>
              <p className="mx-auto mt-4 max-w-xl text-muted-foreground">
                Załóż konto terapeuty w minutę. Bez zobowiązań, bez karty kredytowej.
              </p>
              <div className="mt-8 flex flex-wrap justify-center gap-3">
                <Button size="lg" asChild>
                  <Link href="/register">
                    Zacznij teraz
                    <ArrowRight className="ml-1 h-4 w-4" />
                  </Link>
                </Button>
                <Button size="lg" variant="outline" asChild>
                  <Link href="/login">Logowanie</Link>
                </Button>
              </div>
            </div>
          </div>
        </section>
      </main>

      <footer className="border-t bg-card/30">
        <div className="mx-auto max-w-6xl px-6 py-12">
          <div className="grid gap-8 md:grid-cols-4">
            <div>
              <Link href="/" className="flex items-center gap-2">
                <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-primary to-emerald-500">
                  <HeartPulse className="h-4 w-4 text-white" />
                </div>
                <span className="font-bold">PhysioFlow</span>
              </Link>
              <p className="mt-3 text-xs text-muted-foreground">
                Cyfrowy ekosystem dla fizjoterapii i rehabilitacji.
              </p>
            </div>
            <div>
              <div className="mb-3 text-xs font-semibold uppercase tracking-wider">Produkt</div>
              <ul className="space-y-2 text-sm text-muted-foreground">
                <li><a href="#features" className="hover:text-foreground">Funkcje</a></li>
                <li><a href="#how" className="hover:text-foreground">Jak to działa</a></li>
                <li><a href="#tech" className="hover:text-foreground">Technologia</a></li>
                <li><a href="#faq" className="hover:text-foreground">FAQ</a></li>
              </ul>
            </div>
            <div>
              <div className="mb-3 text-xs font-semibold uppercase tracking-wider">Konto</div>
              <ul className="space-y-2 text-sm text-muted-foreground">
                <li><Link href="/login" className="hover:text-foreground">Zaloguj się</Link></li>
                <li><Link href="/register" className="hover:text-foreground">Załóż konto</Link></li>
              </ul>
            </div>
            <div>
              <div className="mb-3 text-xs font-semibold uppercase tracking-wider">Projekt</div>
              <ul className="space-y-2 text-sm text-muted-foreground">
                <li className="flex items-center gap-2">
                  <Github className="h-3.5 w-3.5" />
                  <a href="https://github.com/lukaszjanus03/physioflow" target="_blank" rel="noreferrer" className="hover:text-foreground">
                    GitHub
                  </a>
                </li>
                <li className="flex items-center gap-2">
                  <Languages className="h-3.5 w-3.5" />
                  <span>Polski</span>
                </li>
              </ul>
            </div>
          </div>
          <div className="mt-12 flex flex-col items-center justify-between gap-2 border-t pt-6 text-xs text-muted-foreground sm:flex-row">
            <span>Hubert Rola &amp; Łukasz Janus</span>
          </div>
        </div>
      </footer>
    </div>
  );
}