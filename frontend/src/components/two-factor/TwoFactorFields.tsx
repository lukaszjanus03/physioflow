'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input, Label } from '@/components/ui/primitives';
import { twoFactorError, type ProofMode } from '@/lib/two-factor-api';

export function useTwoFactorAction() {
  const request = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState<number | null>(null);
  useEffect(
    () => () => {
      request.current?.abort();
      request.current = null;
    },
    [],
  );
  useEffect(() => {
    if (retry === null) return;
    const timer = window.setTimeout(() => setRetry(null), Math.max(0, retry - Date.now()));
    return () => window.clearTimeout(timer);
  }, [retry]);
  async function run<T>(
    work: (signal: AbortSignal) => Promise<T>,
    success: (result: T) => void,
    failure?: (code: string | undefined) => void,
  ) {
    if (request.current || retry !== null) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError('');
    try {
      const result = await work(controller.signal);
      if (!controller.signal.aborted) success(result);
    } catch (cause) {
      if (!controller.signal.aborted) {
        const problem = twoFactorError(cause);
        setError(
          problem.message + (problem.retrySeconds ? ` Spróbuj za około ${problem.retrySeconds} s.` : ''),
        );
        if (problem.retrySeconds) setRetry(Date.now() + problem.retrySeconds * 1000);
        failure?.(problem.code);
      }
    } finally {
      if (request.current === controller) {
        request.current = null;
        setBusy(false);
      }
    }
  }
  return { busy, blocked: busy || retry !== null, error, setError, run };
}

export function useExpired(expiresAt: string) {
  const [expired, setExpired] = useState(() => !(Date.parse(expiresAt) > Date.now()));
  useEffect(() => {
    const remaining = Date.parse(expiresAt) - Date.now();
    const timer = window.setTimeout(
      () => setExpired(true),
      Number.isFinite(remaining) ? Math.max(0, remaining) : 0,
    );
    return () => window.clearTimeout(timer);
  }, [expiresAt]);
  return expired;
}

export function ProofField({
  mode,
  value,
  disabled,
  onChange,
  onToggle,
}: {
  mode: ProofMode;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
  onToggle?: () => void;
}) {
  const id = useId();
  const totp = mode === 'totp';
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{totp ? 'Kod z aplikacji' : 'Kod odzyskiwania'}</Label>
      <Input
        id={id}
        type={totp ? 'text' : 'password'}
        inputMode={totp ? 'numeric' : 'text'}
        autoComplete={totp ? 'one-time-code' : 'off'}
        spellCheck={false}
        autoCapitalize="none"
        maxLength={totp ? 6 : 32}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
      {onToggle && (
        <Button type="button" variant="ghost" size="sm" onClick={onToggle} disabled={disabled}>
          {totp ? 'Użyj kodu odzyskiwania' : 'Użyj kodu z aplikacji'}
        </Button>
      )}
    </div>
  );
}
