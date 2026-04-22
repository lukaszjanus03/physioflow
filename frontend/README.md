# PhysioFlow Frontend

Next.js 15 App Router + TypeScript + Tailwind + shadcn/ui.

## Setup

```bash
cp .env.example .env.local
# ustaw NEXT_PUBLIC_API_URL na adres backendu
npm install
npm run dev
```

## Struktura

```
src/
├── app/
│   ├── (auth)/          # login, register
│   ├── (therapist)/     # panel terapeuty (sidebar)
│   ├── (patient)/       # portal pacjenta
│   ├── layout.tsx       # root layout + Providers
│   └── page.tsx         # redirect based on role
├── components/
│   ├── ui/              # shadcn primitives
│   ├── layout/          # sidebary, nawigacje
│   ├── patients/        # feature components
│   ├── calendar/
│   ├── body-map/        # Konva SVG body mapping
│   ├── therapy/
│   └── chat/            # SignalR chat
├── lib/
│   ├── api-client.ts    # axios + JWT refresh
│   ├── api.ts           # feature API calls
│   ├── providers.tsx    # React Query + Toast + SignalR
│   ├── signalr-context.tsx  # globalny hub provider
│   └── utils.ts
├── hooks/
├── stores/
│   └── auth-store.ts    # Zustand + persist
└── types/
    └── api.ts           # DTOs z backendu
```

## Skrypty

```bash
npm run dev          # dev server
npm run build        # production build
npm run lint         # ESLint
npm run type-check   # tsc --noEmit
npm run format       # Prettier
```

## Konwencje

- Route groups `()` — grupowanie stron bez wpływu na URL (np. `(auth)/login` → `/login`)
- Server Components domyślnie, `'use client'` tylko gdy konieczne
- React Query do serwera, Zustand do klienta (auth, UI state)
- Wszystkie fetche przez `apiClient` z `lib/api-client.ts`
- SignalR tylko przez `useSignalR()` / `useSignalREvent()` — jedno WebSocket per hub
