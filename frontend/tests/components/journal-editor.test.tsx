import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { JournalEditor } from '@/components/patient-journal/JournalEditor';
import { JournalDeleteDialog } from '@/components/patient-journal/JournalDeleteDialog';
import { JournalNavigationProvider } from '@/components/patient-journal/JournalNavigation';
import type { CreateJournalRequest, JournalEntry } from '@/lib/exercise-journal-api';
import { renderWithQuery } from '../render';
import { apiUrl, server } from '../mocks/server';
import { journalEntry, journalTarget } from '../fixtures/journal';
import { useAuthStore } from '@/stores/auth-store';
import { ids } from '../fixtures/sharing';

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
const root = '/api/patient-portal/exercise-journal';
function createEditor(onSaved = vi.fn(), onClose = vi.fn()) {
  renderWithQuery(
    <JournalNavigationProvider>
      <JournalEditor source={{ target: journalTarget }} onClose={onClose} onSaved={onSaved} />
    </JournalNavigationProvider>,
  );
  return { onSaved, onClose };
}

describe('journal create and retry', () => {
  beforeEach(() => useAuthStore.getState().setUser(ids.patient, 'patient@example.test', ['Patient']));

  it('records skipped status with no dose, explicit zero pain and a trimmed comment', async () => {
    const user = userEvent.setup();
    const sent: CreateJournalRequest[] = [];
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        const body = (await request.json()) as CreateJournalRequest;
        sent.push(body);
        return HttpResponse.json({ ...journalEntry, ...body });
      }),
    );
    const { onSaved } = createEditor();
    await user.type(screen.getByLabelText('Wykonane serie (opcjonalnie)'), '2');
    await user.selectOptions(screen.getByLabelText('Status realizacji'), 'Skipped');
    expect(screen.queryByLabelText('Wykonane serie (opcjonalnie)')).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Ocena bólu (opcjonalnie)'), '0');
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), '  Wpis testowy  ');
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(sent[0]).toMatchObject({
      status: 'Skipped',
      actualSets: null,
      actualRepetitions: null,
      actualDurationSeconds: null,
      painLevel: 0,
      comment: 'Wpis testowy',
      planItemId: ids.item,
      planVersionNumber: 1,
    });
    expect(sent[0].requestId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(sent[0]).not.toHaveProperty('exercise');
  });

  it('retries the exact same body and request id after a lost response', async () => {
    const user = userEvent.setup();
    const sent: CreateJournalRequest[] = [];
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        const body = (await request.json()) as CreateJournalRequest;
        sent.push(body);
        return sent.length === 1 ? HttpResponse.error() : HttpResponse.json({ ...journalEntry, ...body });
      }),
    );
    const { onSaved } = createEditor();
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), 'Nie zgub tej treści');
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    const retry = await screen.findByRole('button', { name: 'Ponów identyczne zgłoszenie' });
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toHaveValue('Nie zgub tej treści');
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toBeDisabled();
    expect(onSaved).not.toHaveBeenCalled();
    await user.click(retry);
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(sent).toHaveLength(2);
    expect(sent[1]).toEqual(sent[0]);
  });

  it('keeps fields editable after a definite validation rejection', async () => {
    const user = userEvent.setup();
    server.use(
      http.post(apiUrl(root), () =>
        HttpResponse.json({ code: 'validation_failed', detail: 'PRIVATE_VALUE' }, { status: 400 }),
      ),
    );
    createEditor();
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), 'Zachowaj mnie');
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    await screen.findByRole('alert');
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toHaveValue('Zachowaj mnie');
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toBeEnabled();
    expect(screen.getByRole('alert')).not.toHaveTextContent('PRIVATE_VALUE');
  });

  it('requires a publication check before retrying a create conflict', async () => {
    const user = userEvent.setup();
    const sent: CreateJournalRequest[] = [];
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        const body = (await request.json()) as CreateJournalRequest;
        sent.push(body);
        return sent.length === 1
          ? HttpResponse.json({ code: 'concurrent_modification' }, { status: 409 })
          : HttpResponse.json(journalEntry);
      }),
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () =>
        HttpResponse.json({
          versionNumber: 1,
          publishedAt: journalTarget.publishedAt,
          content: { exercises: [{ id: ids.item }] },
        }),
      ),
    );
    const { onSaved } = createEditor();
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    await screen.findByRole('button', { name: 'Sprawdź aktualne udostępnienie' });
    expect(screen.getByRole('button', { name: 'Ponów identyczne zgłoszenie' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Sprawdź aktualne udostępnienie' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ponów identyczne zgłoszenie' })).toBeEnabled(),
    );
    await user.click(screen.getByRole('button', { name: 'Ponów identyczne zgłoszenie' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(sent[1]).toEqual(sent[0]);
  });

  it('blocks retry when a publication was replaced by another version', async () => {
    const user = userEvent.setup();
    let writes = 0;
    server.use(
      http.post(apiUrl(root), () => {
        writes++;
        return HttpResponse.json({ code: 'concurrent_modification' }, { status: 409 });
      }),
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () =>
        HttpResponse.json({
          versionNumber: 2,
          publishedAt: journalTarget.publishedAt,
          content: { exercises: [{ id: ids.item }] },
        }),
      ),
    );
    createEditor();
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    await user.click(await screen.findByRole('button', { name: 'Sprawdź aktualne udostępnienie' }));
    await screen.findByText(/Udostępnienie zmieniło się/);
    expect(screen.getByRole('button', { name: 'Ponów identyczne zgłoszenie' })).toBeDisabled();
    expect(writes).toBe(1);
  });

  it('keeps the original plan snapshot even if props change while the form is open', async () => {
    const user = userEvent.setup();
    let sent: CreateJournalRequest | undefined;
    server.use(
      http.post(apiUrl(root), async ({ request }) => {
        sent = (await request.json()) as CreateJournalRequest;
        return HttpResponse.json(journalEntry);
      }),
    );
    const onSaved = vi.fn();
    const onClose = vi.fn();
    const { rerender } = renderWithQuery(
      <JournalNavigationProvider>
        <JournalEditor source={{ target: journalTarget }} onClose={onClose} onSaved={onSaved} />
      </JournalNavigationProvider>,
    );
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), 'Moja treść');
    rerender(
      <JournalNavigationProvider>
        <JournalEditor
          source={{ target: { ...journalTarget, planVersionNumber: 2 } }}
          onClose={onClose}
          onSaved={onSaved}
        />
      </JournalNavigationProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(sent?.planVersionNumber).toBe(1);
  });

  it.each(['idempotency_key_reused', 'journal_entry_deleted'])(
    'does not recreate an entry after %s',
    async (code) => {
      const user = userEvent.setup();
      server.use(http.post(apiUrl(root), () => HttpResponse.json({ code }, { status: 409 })));
      createEditor();
      await user.click(screen.getByRole('button', { name: 'Zapisz wykonanie' }));
      await screen.findByRole('alert');
      expect(screen.getByRole('button', { name: 'Ponów identyczne zgłoszenie' })).toBeDisabled();
    },
  );

  it('asks before discarding an edited form', async () => {
    const user = userEvent.setup();
    const { onClose } = createEditor();
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), 'Niezapisana treść');
    await user.click(screen.getByRole('button', { name: 'Zamknij' }));
    expect(screen.getByRole('dialog', { name: 'Niezapisane zmiany' })).toBeVisible();
    expect(onClose).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Zostań w formularzu' }));
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toHaveValue('Niezapisana treść');
    await user.click(screen.getByRole('button', { name: 'Zamknij' }));
    await user.click(screen.getByRole('button', { name: 'Odrzuć zmiany' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('journal edit and delete conflicts', () => {
  it('retains local content and uses the new token only after explicit comparison', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const writes: Array<Record<string, unknown>> = [];
    const fresh: JournalEntry = { ...journalEntry, version: 'journal-token-2', comment: 'SERVER_COMMENT' };
    server.use(
      http.put(apiUrl(`${root}/${journalEntry.id}`), async ({ request }) => {
        writes.push((await request.json()) as Record<string, unknown>);
        return writes.length === 1
          ? HttpResponse.json({ code: 'concurrent_modification' }, { status: 409 })
          : HttpResponse.json({ ...fresh, comment: 'LOCAL_COMMENT', version: 'journal-token-3' });
      }),
      http.get(apiUrl(`${root}/${journalEntry.id}`), () => HttpResponse.json(fresh)),
    );
    renderWithQuery(
      <JournalNavigationProvider>
        <JournalEditor source={{ entry: journalEntry }} onClose={vi.fn()} onSaved={onSaved} />
      </JournalNavigationProvider>,
    );
    await user.clear(screen.getByLabelText('Komentarz (opcjonalnie)'));
    await user.type(screen.getByLabelText('Komentarz (opcjonalnie)'), 'LOCAL_COMMENT');
    await user.click(screen.getByRole('button', { name: 'Zapisz zmiany' }));
    await user.click(await screen.findByRole('button', { name: 'Pobierz aktualny wpis do porównania' }));
    await screen.findByText('SERVER_COMMENT');
    expect(screen.getByLabelText('Komentarz (opcjonalnie)')).toHaveValue('LOCAL_COMMENT');
    expect(writes).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Zachowaj mój formularz' })).toBeDisabled();
    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: 'Zachowaj mój formularz' }));
    expect(writes).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Zapisz zmiany' }));
    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(writes[1]).toMatchObject({ version: 'journal-token-2', comment: 'LOCAL_COMMENT' });
  });

  it('requires a new confirmation before deleting a concurrently edited entry', async () => {
    const user = userEvent.setup();
    const deleted = vi.fn();
    const versions: unknown[] = [];
    server.use(
      http.delete(apiUrl(`${root}/${journalEntry.id}`), async ({ request }) => {
        versions.push(await request.json());
        return versions.length === 1
          ? HttpResponse.json({ code: 'concurrent_modification' }, { status: 409 })
          : new HttpResponse(null, { status: 204 });
      }),
      http.get(apiUrl(`${root}/${journalEntry.id}`), () =>
        HttpResponse.json({ ...journalEntry, version: 'fresh-delete-token', comment: 'Zmiana na serwerze' }),
      ),
    );
    renderWithQuery(
      <JournalNavigationProvider>
        <JournalDeleteDialog entry={journalEntry} onClose={vi.fn()} onDeleted={deleted} />
      </JournalNavigationProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'Potwierdzam usunięcie' }));
    await user.click(await screen.findByRole('button', { name: 'Pobierz aktualny wpis' }));
    await screen.findByText('Zmiana na serwerze');
    expect(deleted).not.toHaveBeenCalled();
    expect(versions).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Potwierdzam usunięcie' }));
    await waitFor(() => expect(deleted).toHaveBeenCalledTimes(1));
    expect(versions[1]).toEqual({ version: 'fresh-delete-token' });
  });

  it('shows missing pain as different from zero', () => {
    renderWithQuery(
      <JournalDeleteDialog entry={{ ...journalEntry, painLevel: 0 }} onClose={vi.fn()} onDeleted={vi.fn()} />,
    );
    expect(within(screen.getByRole('dialog')).getByText(/0\/10 — brak bólu/)).toBeVisible();
  });
});
