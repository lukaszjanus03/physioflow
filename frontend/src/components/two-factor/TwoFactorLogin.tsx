'use client';

import { useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  parseProof,
  twoFactorApi,
  type LoginChallenge,
  type LoginSession,
  type ProofMode,
} from '@/lib/two-factor-api';
import { ProofField, useExpired, useTwoFactorAction } from './TwoFactorFields';

export function TwoFactorLogin({
  challenge,
  complete,
  back,
}: {
  challenge: LoginChallenge;
  complete: (session: LoginSession) => void;
  back: () => void;
}) {
  const expired = useExpired(challenge.expiresAt);
  const [invalid, setInvalid] = useState(false);
  const [mode, setMode] = useState<ProofMode>('totp');
  const [value, setValue] = useState('');
  const action = useTwoFactorAction();
  const proof = parseProof(mode, value);
  const canVerify = !expired && !invalid;
  return (
    <section className="space-y-4 rounded-2xl border bg-card p-5 shadow-sm sm:p-6">
      <ShieldCheck className="text-primary" aria-hidden="true" />
      <h1 className="text-3xl">Zweryfikuj logowanie</h1>
      {canVerify ? (
        <form
          className="space-y-4"
          aria-busy={action.busy}
          onSubmit={(event) => {
            event.preventDefault();
            if (!proof || Date.parse(challenge.expiresAt) <= Date.now()) return;
            setValue('');
            void action.run(
              (signal) => twoFactorApi.verify(challenge.challengeToken, proof, signal),
              complete,
              (code) => {
                if (
                  code === 'two_factor_invalid_challenge' ||
                  code === 'concurrent_modification' ||
                  code === 'email_confirmation_required'
                )
                  setInvalid(true);
              },
            );
          }}
        >
          <p className="text-sm text-muted-foreground">
            {mode === 'totp'
              ? 'Wpisz sześciocyfrowy kod z aplikacji uwierzytelniającej.'
              : 'Wpisz jeden z zapisanych jednorazowych kodów odzyskiwania.'}
          </p>
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
          <Button type="submit" disabled={action.blocked || !proof}>
            {action.busy ? 'Sprawdzanie…' : 'Potwierdź logowanie'}
          </Button>
        </form>
      ) : (
        <p role="status" className="text-sm text-muted-foreground">
          Rozpocznij logowanie ponownie, aby uzyskać nową próbę weryfikacji.
        </p>
      )}
      {action.error && (
        <p role="alert" className="text-sm text-destructive">
          {action.error}
        </p>
      )}
      <Button type="button" variant="outline" onClick={back}>
        Wróć do logowania
      </Button>
    </section>
  );
}
