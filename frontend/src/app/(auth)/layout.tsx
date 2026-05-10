import { ArrowLeft, HeartPulse } from 'lucide-react';
import Link from 'next/link';

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="relative flex min-h-screen flex-col bg-background">
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

      <header className="px-6 py-5">
        <div className="mx-auto flex max-w-6xl items-center justify-between">
          <Link href="/" className="flex items-center gap-2 transition-opacity hover:opacity-80">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-primary to-emerald-500 shadow-lg shadow-primary/30">
              <HeartPulse className="h-4 w-4 text-white" />
            </div>
            <span className="text-lg font-bold">PhysioFlow</span>
          </Link>
          <Link
            href="/"
            className="flex items-center gap-1 text-sm text-muted-foreground transition-colors hover:text-foreground"
          >
            <ArrowLeft className="h-3.5 w-3.5" />
            Strona główna
          </Link>
        </div>
      </header>

      <main className="flex flex-1 items-center justify-center p-4">
        <div className="w-full max-w-md">{children}</div>
      </main>

      <footer className="px-6 py-6 text-center text-xs text-muted-foreground">
        © PhysioFlow — praca inżynierska 2026
      </footer>
    </div>
  );
}