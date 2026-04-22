# Specyfikacja techniczna

## Frontend — Next.js hostowany na Vercel

- **TypeScript** — język programowania
- **Tailwind CSS** — stylowanie
- **shadcn/ui** — komponenty UI (Radix + Tailwind)
- **React Query (TanStack)** — pobieranie i cache'owanie danych z API
- **Zustand** — zarządzanie stanem aplikacji (auth)
- **Konva.js** — interaktywna mapa ciała (Body Mapping)
- **Recharts** — wykresy i statystyki
- **Tiptap** — edytor rich text do notatek EDM
- **React Big Calendar** — widok kalendarza
- **@react-pdf/renderer** — generowanie PDF (karta pacjenta po stronie klienta)
- **@microsoft/signalr** — klient realtime (czat, powiadomienia)

## Backend — .NET 8 hostowany na Azure App Service

- **C#** — język programowania
- **ASP.NET Core Web API** — REST API
- **Entity Framework Core** + **Npgsql** — ORM dla PostgreSQL
- **ASP.NET Core Identity** — autentykacja, role, zarządzanie hasłami
- **JWT Bearer** — tokeny dostępu + refresh tokens
- **SignalR** — czat i powiadomienia w czasie rzeczywistym
- **MediatR** — wzorzec CQRS (commands/queries)
- **FluentValidation** — walidacja danych wejściowych
- **AutoMapper** — mapowanie DTO ↔ encje
- **Serilog** — strukturalne logowanie
- **Swagger / OpenAPI** — dokumentacja API

## Baza danych — PostgreSQL

- Hostowanie: **Azure Database for PostgreSQL** lub **Neon.tech** (free tier)
- Migracje: EF Core Migrations
- Izolacja danych pacjentów: wymuszana w warstwie aplikacji (filtry po `TherapistId`)
- Soft delete: global query filter w EF Core (`IsDeleted = false`)

## Storage — Cloudflare R2

- S3-compatible, free do 10 GB
- Klient: AWSSDK.S3 z customowym endpointem
- Pre-signed URLs do pobierania plików bez tunelowania przez backend
- Scope: zdjęcia ćwiczeń, załączniki EDM (PDF, RTG), wideo

## Pozostałe usługi

- **Resend** — wysyłanie e-maili (przypomnienia o wizytach, zaproszenia pacjentów)
- **GitHub Actions** — automatyczny build i test (CI)
- **Vercel** — hosting frontendu (free tier)
- **Azure App Service** — hosting backendu (12 miesięcy free)
- **Cloudflare** — domena + DNS

## Architektura

```
┌────────────────┐         ┌──────────────────┐         ┌──────────────┐
│  Next.js       │  HTTPS  │  .NET 8 Web API  │   EF    │  PostgreSQL  │
│  (Vercel)      │────────▶│  (Azure)         │────────▶│              │
│                │◀────────│                  │         └──────────────┘
│  React Query   │   JSON  │  MediatR+CQRS    │
│  Zustand       │         │                  │         ┌──────────────┐
│  SignalR cli.  │◀═ WS ══▶│  SignalR Hub     │         │ Cloudflare R2│
└────────────────┘         │                  │────────▶│ (storage)    │
                           │                  │         └──────────────┘
                           │                  │
                           │                  │         ┌──────────────┐
                           │                  │────────▶│ Resend       │
                           └──────────────────┘         │ (email)      │
                                                        └──────────────┘
```

## Warstwy backendu (Clean Architecture)

```
PhysioFlow.Api              ← kontrolery, middleware, hub'y SignalR
    ↓ depends on
PhysioFlow.Infrastructure   ← EF Core, Identity, R2, Resend
    ↓ depends on
PhysioFlow.Application      ← MediatR handlery, walidacja, DTO
    ↓ depends on
PhysioFlow.Domain           ← encje, enumy (zero zależności)
```

Infrastructure implementuje interfejsy z Application (`IStorageService`, `IEmailService`, `ICurrentUserService`, `IApplicationDbContext`) — klasyczna Dependency Inversion.
