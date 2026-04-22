# ADR-001: Wybór stacku technologicznego

- **Data:** 2026-04-22
- **Status:** accepted
- **Autorzy:** Hubert Rola, Łukasz Janus

## Kontekst

PhysioFlow to praca inżynierska — oprócz działającego produktu, ważny jest też aspekt dydaktyczny: chcemy poznać "klasyczny" enterprise stack .NET i nowoczesny stack frontendowy. System obsługuje wrażliwe dane medyczne (RODO), więc bezpieczeństwo, izolacja danych i możliwość audytu są kluczowe.

## Rozważane opcje

### Backend

- **A. .NET 8 + EF Core + PostgreSQL + ASP.NET Identity** (wybrana)
- **B. Next.js fullstack + Supabase (Auth + Postgres + RLS + Storage)**
- **C. Node.js (NestJS) + Prisma + PostgreSQL**

### Frontend

- **A. Next.js 15 App Router + Tailwind + shadcn/ui** (wybrana)
- **B. Vite + React SPA + MUI**
- **C. SvelteKit**

## Decyzja

Wybieramy **Backend A** i **Frontend A**.

### Uzasadnienie backend

- **Dydaktyka:** .NET jest standardem enterprise w Polsce — praca inżynierska na tym stacku ma sensowną przewagę rynkową. Pozwala nam nauczyć się Clean Architecture, CQRS (MediatR), EF Core.
- **Typesafe end-to-end:** C# + TypeScript to dwa mocno typowane języki — kontrakty API łatwe do utrzymania (i ewentualnej automatycznej generacji klienta z OpenAPI).
- **ASP.NET Identity** daje nam za darmo: hashowanie haseł, lockout, role, confirmation emails. Nie musimy wymyślać koła na nowo.
- **SignalR** nativnie wspierany w .NET — bez potrzeby Socket.IO / Pusher.
- **Supabase (opcja B)** byłby szybszy do MVP, ale z własnym backendem .NET tracimy RLS (bo backend jest service-role), a jednocześnie płacimy za narzut Supabase. Lepiej lecieć z gołym Postgresem.

### Uzasadnienie frontend

- **Next.js App Router:** SSR dla publicznych stron (landing, rezerwacja online), Client Components dla paneli terapeuty/pacjenta.
- **shadcn/ui:** nie jest biblioteką — to kopiowanie komponentów do repo, pełna kontrola i customizacja. WCAG 2.1 dostajemy z Radix UI za darmo.
- **React Query** oddziela stan serwera od stanu klienta — cache, invalidation, retries bez ręcznego kodu.
- **Zustand** zamiast Reduxa — mniej boilerplate'u, a do auth store'a wystarczy.

## Konsekwencje

**Pozytywne:**

- Klasyczny, dobrze udokumentowany stack — łatwy do obrony pracy
- Skalowalność pionowa (Azure App Service) i pozioma (bezstanowy API)
- Silny typing po obu stronach
- Dobra historia pod CI/CD (`dotnet test`, `npm run build`)

**Negatywne / kompromisy:**

- Więcej kodu niż w wariancie Supabase — ręcznie piszemy auth controller, repo interfejsy
- Dwa osobne deploymenty (Vercel + Azure) zamiast jednego
- Cold start Azure App Service w free tier może być problematyczny przy demonstracji

**Do zrobienia:**

- Monitoring po wejściu na produkcję (Application Insights albo Seq)
- Automatyczny generator klienta TS z OpenAPI (NSwag) — odłożone do momentu stabilizacji kontraktów
