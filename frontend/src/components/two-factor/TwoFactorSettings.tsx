'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import { usePatientContent } from '@/hooks/use-patient-content';
import { parseProof, twoFactorApi, type ProofMode, type TwoFactorSetup } from '@/lib/two-factor-api';
import { useAuthStore } from '@/stores/auth-store';
import { ProofField, useExpired, useTwoFactorAction } from './TwoFactorFields';
import { useTwoFactorFlow } from './two-factor-flow-context';

function OfflineQr({ uri }: { uri: string }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const target = canvas.current;
    let active = true;
    void import('qrcode')
      .then(({ toCanvas }) => {
        if (active && target)
          return toCanvas(target, uri, { width: 220, margin: 4, errorCorrectionLevel: 'M' });
      })
      .catch(() => {
        if (active) setFailed(true);
      });
    return () => {
      active = false;
    };
  }, [uri]);
  return failed ? (
    <p role="status">Nie udało się wyświetlić QR. Użyj klucza poniżej.</p>
  ) : (
    <canvas
      ref={canvas}
      role="img"
      aria-label="Kod QR konfiguracji aplikacji uwierzytelniającej"
      className="mx-auto max-w-full rounded-lg"
    />
  );
}

function ConfirmSetup({
  setup,
  accountId,
  back,
}: {
  setup: TwoFactorSetup;
  accountId: string;
  back: () => void;
}) {
  const [value, setValue] = useState('');
  const [restart, setRestart] = useState(false);
  const action = useTwoFactorAction();
  const expired = useExpired(setup.expiresAt);
  const { resetSession: reset } = useTwoFactorFlow();
  const proof = parseProof('totp', value);
  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (expired || restart || !proof?.code) return;
        const code = proof.code;
        setValue('');
        void action.run(
          (signal) => twoFactorApi.confirm(code, signal),
          ({ recoveryCodes }) => reset(accountId, recoveryCodes),
          (problem) => {
            if (
              problem === 'two_factor_setup_expired' ||
              problem === 'two_factor_already_enabled' ||
              problem === 'concurrent_modification'
            )
              setRestart(true);
          },
        );
      }}
    >
      {expired || restart ? (
        <p role="status" className="text-sm text-muted-foreground">
          Rozpocznij konfigurację ponownie lub sprawdź aktualny stan konta.
        </p>
      ) : (
        <>
          <p className="text-sm leading-6">
            Zeskanuj QR w aplikacji uwierzytelniającej, np. Google Authenticator lub Microsoft Authenticator.
            Jeśli używasz tego samego telefonu, wpisz klucz ręcznie jako klucz czasowy (TOTP).
          </p>
          <OfflineQr uri={setup.authenticatorUri} />
          <div className="rounded-lg border bg-muted/30 p-3">
            <p className="text-xs text-muted-foreground">Klucz do ręcznego wpisania</p>
            <code className="break-all text-sm">{setup.sharedKey}</code>
          </div>
          <ProofField mode="totp" value={value} disabled={action.busy} onChange={setValue} />
        </>
      )}
      {action.error && (
        <p role="alert" className="text-sm text-destructive">
          {action.error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {!expired && !restart && (
          <Button type="submit" disabled={action.blocked || !proof}>
            {action.busy ? 'Sprawdzanie…' : 'Potwierdź i włącz 2FA'}
          </Button>
        )}
        <Button type="button" variant="outline" onClick={back} disabled={action.busy}>
          {expired || restart ? 'Wróć do konfiguracji' : 'Anuluj konfigurację'}
        </Button>
      </div>
    </form>
  );
}

function SetupFlow({ accountId, onContinue }: { accountId: string; onContinue?: () => void }) {
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null);
  const action = useTwoFactorAction();
  if (setup) return <ConfirmSetup setup={setup} accountId={accountId} back={() => setSetup(null)} />;
  return (
    <div className="space-y-3">
      <p className="text-sm leading-6 text-muted-foreground">
        {onContinue
          ? 'Zabezpiecz konto dodatkowym kodem z aplikacji. Możesz zrobić to teraz lub później w ustawieniach.'
          : 'Do logowania oprócz hasła potrzebny będzie kod z aplikacji uwierzytelniającej.'}
      </p>
      {action.error && (
        <p role="alert" className="text-sm text-destructive">
          {action.error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          disabled={action.blocked}
          onClick={() => void action.run(twoFactorApi.setup, setSetup)}
        >
          {action.busy ? 'Proszę czekać…' : 'Włącz 2FA'}
        </Button>
        {onContinue && (
          <Button
            type="button"
            variant="outline"
            disabled={action.busy}
            onClick={onContinue}
            className="h-auto min-h-10 whitespace-normal text-left"
          >
            Przypomnij przy następnym logowaniu
          </Button>
        )}
      </div>
    </div>
  );
}

export function TwoFactorPrompt({ accountId, onContinue }: { accountId: string; onContinue: () => void }) {
  return (
    <section className="space-y-4 rounded-2xl border bg-card p-5 shadow-sm sm:p-6">
      <ShieldCheck className="text-primary" aria-hidden="true" />
      <h1 className="text-3xl">Zabezpiecz swoje konto</h1>
      <SetupFlow accountId={accountId} onContinue={onContinue} />
    </section>
  );
}

function SecurityChange({
  accountId,
  kind,
  back,
  reload,
}: {
  accountId: string;
  kind: 'regenerate' | 'disable';
  back: () => void;
  reload: () => void;
}) {
  const passwordId = useId();
  const [password, setPassword] = useState('');
  const [value, setValue] = useState('');
  const [mode, setMode] = useState<ProofMode>('totp');
  const [stale, setStale] = useState(false);
  const action = useTwoFactorAction();
  const { resetSession: reset } = useTwoFactorFlow();
  const proof = parseProof(mode, value);
  const regenerate = kind === 'regenerate';
  return (
    <form
      className="space-y-4 rounded-xl border p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!password || !proof || stale) return;
        setPassword('');
        setValue('');
        const failed = (code: string | undefined) => {
          if (code === 'concurrent_modification' || code === 'two_factor_not_enabled') setStale(true);
        };
        if (regenerate)
          void action.run(
            (signal) => twoFactorApi.regenerate(password, proof, signal),
            ({ recoveryCodes }) => reset(accountId, recoveryCodes),
            failed,
          );
        else
          void action.run(
            (signal) => twoFactorApi.disable(password, proof, signal),
            () => reset(accountId),
            failed,
          );
      }}
    >
      <h3 className="text-lg font-semibold">{regenerate ? 'Wygeneruj nowe kody' : 'Wyłącz 2FA'}</h3>
      <p className="text-sm text-muted-foreground">
        {regenerate
          ? 'Poprzednie kody stracą ważność.'
          : 'Następne logowanie nie będzie wymagało drugiego kroku.'}{' '}
        Po zmianie zalogujesz się ponownie.
      </p>
      {!stale && (
        <>
          <div className="space-y-2">
            <Label htmlFor={passwordId}>Aktualne hasło</Label>
            <Input
              id={passwordId}
              type="password"
              autoComplete="current-password"
              maxLength={1024}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              disabled={action.busy}
            />
          </div>
          <ProofField
            mode={mode}
            value={value}
            disabled={action.busy}
            onChange={setValue}
            onToggle={() => {
              setMode(mode === 'totp' ? 'recovery' : 'totp');
              setValue('');
            }}
          />
        </>
      )}
      {action.error && (
        <p role="alert" className="text-sm text-destructive">
          {action.error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        {stale ? (
          <Button type="button" onClick={reload}>
            Odśwież stan zabezpieczeń
          </Button>
        ) : (
          <Button type="submit" disabled={!password || !proof || action.blocked}>
            {action.busy ? 'Sprawdzanie…' : 'Potwierdź zmianę'}
          </Button>
        )}
        <Button type="button" variant="outline" onClick={back} disabled={action.busy}>
          Anuluj
        </Button>
      </div>
    </form>
  );
}

function SecurityStatus({ accountId }: { accountId: string }) {
  const status = usePatientContent(twoFactorApi.status, true);
  const [change, setChange] = useState<'regenerate' | 'disable' | null>(null);
  if (status.loading)
    return (
      <p role="status" className="text-sm">
        Wczytywanie zabezpieczeń…
      </p>
    );
  if (!status.data)
    return (
      <div className="space-y-3">
        <p role="alert" className="text-sm text-destructive">
          Nie udało się pobrać stanu zabezpieczeń.
        </p>
        <Button type="button" variant="outline" onClick={status.reload}>
          Spróbuj ponownie
        </Button>
      </div>
    );
  return (
    <div className="space-y-4">
      <p className="text-sm" role="status">
        Weryfikacja dwuetapowa: <strong>{status.data.enabled ? 'włączona' : 'wyłączona'}</strong>.
      </p>
      {!status.data.enabled ? (
        <SetupFlow accountId={accountId} />
      ) : (
        <>
          <p className="text-sm text-muted-foreground">
            Pozostałe kody odzyskiwania: {status.data.recoveryCodesRemaining}.
          </p>
          {change ? (
            <SecurityChange
              accountId={accountId}
              kind={change}
              back={() => setChange(null)}
              reload={() => {
                setChange(null);
                status.reload();
              }}
            />
          ) : (
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="outline" onClick={() => setChange('regenerate')}>
                Wygeneruj nowe kody odzyskiwania
              </Button>
              <Button type="button" variant="outline" onClick={() => setChange('disable')}>
                Wyłącz 2FA
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

export function TwoFactorSettings() {
  const userId = useAuthStore((state) => (state.isAuthenticated ? state.userId : null));
  const [open, setOpen] = useState(false);
  const id = useId();
  if (!userId) return null;
  return (
    <section aria-labelledby={id} className="space-y-4 rounded-2xl border bg-card p-5 sm:p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id={id} className="flex items-center gap-2 text-2xl">
          <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
          Bezpieczeństwo konta
        </h2>
        {!open && (
          <Button type="button" variant="outline" onClick={() => setOpen(true)}>
            Zarządzaj 2FA
          </Button>
        )}
      </div>
      {open && <SecurityStatus key={userId} accountId={userId} />}
    </section>
  );
}
