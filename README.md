# PhysioFlow

Kompletny ekosystem cyfrowy dla fizjoterapii i rehabilitacji sportowej — praca inżynierska.

## Stack

**Frontend** — Next.js 15 (App Router) + TypeScript + Tailwind + shadcn/ui
**Backend** — .NET 8 Web API + EF Core + ASP.NET Core Identity + JWT + SignalR
**Baza** — PostgreSQL 16
**Storage** — Cloudflare R2 (S3-compatible)
**E-mail** — Resend
**CI/CD** — GitHub Actions
**Hosting** — Vercel (frontend) + Azure App Service (backend)

## Struktura repo

```
physioflow/
├── frontend/          # Next.js 15 app
├── backend/           # .NET 8 solution
├── docs/              # dokumentacja techniczna, ADR, diagramy
├── .github/workflows/ # CI/CD pipelines
└── README.md
```

## Szybki start

### Wymagania
- Node.js 20+
- .NET 8 SDK
- PostgreSQL 16 (lokalnie albo Docker)
- Git

### Setup lokalny

```bash
# 1. Baza w Dockerze
docker run -d --name physioflow-db \
  -e POSTGRES_USER=physioflow \
  -e POSTGRES_PASSWORD=dev \
  -e POSTGRES_DB=physioflow \
  -p 5432:5432 postgres:16

# 2. Backend
cd backend
cp PhysioFlow.Api/appsettings.Development.example.json PhysioFlow.Api/appsettings.Development.json
dotnet restore
dotnet ef database update --project PhysioFlow.Infrastructure --startup-project PhysioFlow.Api
dotnet run --project PhysioFlow.Api
# API: https://localhost:5001 | Swagger: https://localhost:5001/swagger

# 3. Frontend
cd frontend
cp .env.example .env.local
npm install
npm run dev
# App: http://localhost:3000
```

## Dokumentacja

- [Sprint 8, etap 2 — statystyki gabinetu i wykres zgłoszonego bólu (kontrakt backendu)](docs/sprint-8-stage-2-analytics-api.md)
- [Sprint 8, etap 1 — zatwierdzona dokumentacja wizyt do eksportu PDF (kontrakt backendu)](docs/sprint-8-stage-1-medical-record-export-api.md)
- [Sprint 7, etap 2 — podgląd dziennika przez terapeutę i podsumowanie (kontrakt backendu)](docs/sprint-7-stage-2-therapist-exercise-journal-api.md)
- [Sprint 7, etap 1 — dziennik realizacji ćwiczeń (kontrakt backendu)](docs/sprint-7-stage-1-exercise-journal-api.md)
- [Sprint 6 — wideo w ćwiczeniach i wersjonowanych planach, konfiguracja R2](docs/sprint-6-exercise-videos-api.md)
- [Terapia — biblioteka ćwiczeń i wersjonowane plany pacjentów (kontrakt backendu)](docs/stage-2-therapy-api.md)
- [Sprint 5.5 — profile terapeutów i zdjęcia (backend)](docs/sprint-5.5-therapist-profiles-api.md)
- [Sprint 5.5 — publiczny katalog, filtry i ulubieni (backend)](docs/sprint-5.5-public-catalog-favorites-api.md)

- [Specyfikacja techniczna](docs/technical-spec.md)
- [Specyfikacja funkcjonalna](docs/functional-spec.md)
- [Sprint 5 — API wizyt pacjenta i przypomnienia](docs/sprint-5-calendar-api.md)
- [ADR — Architecture Decision Records](docs/adr/)

## Zespół

- Hubert Rola
- Łukasz Janus

## Konwencje

- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/)
- **Branche**: `main` (prod) / `dev` (integracja) / `feature/*` / `fix/*`
- **PR**: wymagany review, CI musi przejść
