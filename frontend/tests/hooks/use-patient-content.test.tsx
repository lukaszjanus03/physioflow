import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usePatientContent } from '@/hooks/use-patient-content';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
}

describe('usePatientContent privacy and lifecycle', () => {
  beforeEach(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
  });

  it('starts with no content while loading', async () => {
    const request = deferred<string>();
    const load = vi.fn(() => request.promise);
    const { result } = renderHook(() => usePatientContent(load));
    expect(result.current.loading).toBe(true);
    expect(result.current.data).toBeNull();
    await act(async () => request.resolve('Public content'));
    expect(result.current.data).toBe('Public content');
    expect(result.current.loading).toBe(false);
  });

  it('does not expose a previous resource when the loader changes', async () => {
    const first = vi.fn(async () => 'PATIENT_A');
    const next = deferred<string>();
    const second = vi.fn(() => next.promise);
    const { result, rerender } = renderHook(({ load }) => usePatientContent(load), {
      initialProps: { load: first },
    });
    await waitFor(() => expect(result.current.data).toBe('PATIENT_A'));
    rerender({ load: second });
    expect(result.current.data).toBeNull();
    expect(result.current.loading).toBe(true);
    await act(async () => next.resolve('PATIENT_B'));
    expect(result.current.data).toBe('PATIENT_B');
  });

  it('ignores a late response from an earlier resource', async () => {
    const old = deferred<string>();
    const first = vi.fn(() => old.promise);
    const second = vi.fn(async () => 'CURRENT_PATIENT');
    const { result, rerender } = renderHook(({ load }) => usePatientContent(load), {
      initialProps: { load: first },
    });
    rerender({ load: second });
    await waitFor(() => expect(result.current.data).toBe('CURRENT_PATIENT'));
    await act(async () => old.resolve('OLD_PATIENT'));
    expect(result.current.data).toBe('CURRENT_PATIENT');
  });

  it('aborts in-flight work when unmounted', () => {
    let signal: AbortSignal | undefined;
    const load = vi.fn((next: AbortSignal) => {
      signal = next;
      return new Promise<string>(() => {});
    });
    const { unmount } = renderHook(() => usePatientContent(load));
    unmount();
    expect(signal?.aborted).toBe(true);
  });

  it('hides old content during an explicit refresh', async () => {
    const next = deferred<string>();
    const load = vi
      .fn()
      .mockResolvedValueOnce('OLD')
      .mockImplementationOnce(() => next.promise);
    const { result } = renderHook(() => usePatientContent(load));
    await waitFor(() => expect(result.current.data).toBe('OLD'));
    act(() => result.current.reload());
    expect(result.current.data).toBeNull();
    expect(result.current.loading).toBe(true);
    await act(async () => next.resolve('NEW'));
    expect(result.current.data).toBe('NEW');
  });

  it.each([401, 403, 404])('removes content and stops automatic reads after HTTP %s', async (status) => {
    const error = { isAxiosError: true, response: { status } };
    const load = vi
      .fn()
      .mockResolvedValueOnce('CLINICAL_CONTENT')
      .mockRejectedValueOnce(error)
      .mockResolvedValue('SHOULD_NOT_LOAD');
    const { result } = renderHook(() => usePatientContent(load));
    await waitFor(() => expect(result.current.data).toBe('CLINICAL_CONTENT'));
    act(() => window.dispatchEvent(new Event('focus')));
    await waitFor(() => expect(result.current.error).toEqual(error));
    expect(result.current.data).toBeNull();
    expect(result.current.loading).toBe(false);
    act(() => {
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
    });
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('allows an explicit retry after access was denied', async () => {
    const load = vi
      .fn()
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 404 } })
      .mockResolvedValueOnce('RESTORED');
    const { result } = renderHook(() => usePatientContent(load));
    await waitFor(() => expect(result.current.error).toBeTruthy());
    act(() => result.current.reload());
    await waitFor(() => expect(result.current.data).toBe('RESTORED'));
    expect(result.current.error).toBeNull();
  });

  it('clears content when a player reports lost access', async () => {
    const load = vi.fn(async () => 'PRIVATE_PLAN');
    const { result } = renderHook(() => usePatientContent(load));
    await waitFor(() => expect(result.current.data).toBe('PRIVATE_PLAN'));
    act(() => result.current.loseAccess());
    expect(result.current.data).toBeNull();
    expect(result.current.error).toMatchObject({ response: { status: 404 } });
  });

  it('does not start overlapping refreshes', async () => {
    const request = deferred<string>();
    const load = vi.fn(() => request.promise);
    renderHook(() => usePatientContent(load));
    act(() => {
      window.dispatchEvent(new Event('focus'));
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
    });
    expect(load).toHaveBeenCalledTimes(1);
    await act(async () => request.resolve('DONE'));
  });

  it('polls visible views but not hidden tabs and removes timers on unmount', async () => {
    vi.useFakeTimers();
    const load = vi.fn(async () => 'PUBLIC');
    const { unmount } = renderHook(() => usePatientContent(load));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(load).toHaveBeenCalledTimes(2);
    Object.defineProperty(document, 'hidden', { configurable: true, value: true });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(60000);
    });
    expect(load).toHaveBeenCalledTimes(2);
    unmount();
    await vi.advanceTimersByTimeAsync(60000);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('pauses background refresh while a journal form is open without clearing content', async () => {
    vi.useFakeTimers();
    const load = vi.fn(async () => 'PUBLIC');
    const { result, rerender } = renderHook(({ paused }) => usePatientContent(load, paused), {
      initialProps: { paused: false },
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    rerender({ paused: true });
    act(() => window.dispatchEvent(new Event('focus')));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(60000);
    });
    expect(load).toHaveBeenCalledTimes(1);
    expect(result.current.data).toBe('PUBLIC');
    rerender({ paused: false });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('ignores an in-flight background response after editing starts', async () => {
    const background = deferred<string>();
    const load = vi
      .fn()
      .mockResolvedValueOnce('ORIGINAL_SNAPSHOT')
      .mockImplementationOnce(() => background.promise);
    const { result, rerender } = renderHook(({ paused }) => usePatientContent(load, paused), {
      initialProps: { paused: false },
    });
    await waitFor(() => expect(result.current.data).toBe('ORIGINAL_SNAPSHOT'));
    act(() => window.dispatchEvent(new Event('focus')));
    expect(load).toHaveBeenCalledTimes(2);
    rerender({ paused: true });
    await act(async () => background.resolve('DIFFERENT_PUBLISHED_VERSION'));
    expect(result.current.data).toBe('ORIGINAL_SNAPSHOT');
  });

  it('does not persist clinical payloads in browser storage', async () => {
    const load = vi.fn(async () => 'CLINICAL_STORAGE_SENTINEL');
    const { result } = renderHook(() => usePatientContent(load));
    await waitFor(() => expect(result.current.data).toBe('CLINICAL_STORAGE_SENTINEL'));
    expect(JSON.stringify(localStorage)).not.toContain('CLINICAL_STORAGE_SENTINEL');
    expect(JSON.stringify(sessionStorage)).not.toContain('CLINICAL_STORAGE_SENTINEL');
  });
});
