import { ArrowRight, HeartPulse, Stethoscope, UserRound } from 'lucide-react';
import Link from 'next/link';

const options = [
  {
    href: '/register/therapist',
    title: 'Jestem terapeutą',
    description:
      'Zarządzaj kartami pacjentów, dokumentacją i przebiegiem terapii.',
    icon: Stethoscope,
  },
  {
    href: '/register/patient',
    title: 'Jestem pacjentem',
    description:
      'Znajdź terapeutę i korzystaj ze swojego portalu pacjenta.',
    icon: UserRound,
  },
];

export default function RegisterPage() {
  return (
    <div className="rounded-2xl border bg-card p-6 shadow-xl shadow-primary/5 sm:p-8">
      <div className="mb-8 text-center">
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-primary text-primary-foreground">
          <HeartPulse aria-hidden="true" className="h-6 w-6" />
        </div>

        <h1 className="text-3xl">Dołącz do PhysioFlow</h1>

        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
          Wybierz rodzaj konta, które chcesz utworzyć.
        </p>
      </div>

      <div className="space-y-4">
        {options.map((option) => {
          const Icon = option.icon;

          return (
            <Link
              key={option.href}
              href={option.href}
              className="group flex items-start gap-4 rounded-xl border p-5 transition-colors hover:border-primary/50 hover:bg-primary/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
            >
              <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
                <Icon aria-hidden="true" className="h-5 w-5" />
              </span>

              <div className="flex-1">
                <h2 className="text-2xl">{option.title}</h2>

                <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
                  {option.description}
                </p>
              </div>

              <ArrowRight
                aria-hidden="true"
                className="mt-3 h-4 w-4 shrink-0 text-muted-foreground transition-colors group-hover:text-primary"
              />
            </Link>
          );
        })}
      </div>

      <p className="mt-6 text-center text-sm text-muted-foreground">
        Masz już konto?{' '}
        <Link href="/login" className="font-medium text-primary hover:underline">
          Zaloguj się
        </Link>
      </p>
    </div>
  );
}