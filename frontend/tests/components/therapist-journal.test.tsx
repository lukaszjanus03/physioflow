import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { delay, http, HttpResponse } from 'msw';
import TherapistJournal from '@/components/therapist-journal/TherapistJournal';
import { useAuthStore } from '@/stores/auth-store';
import { renderWithQuery } from '../render';
import { apiUrl, server } from '../mocks/server';
import { ids, pageOf } from '../fixtures/sharing';
import { journalEntry } from '../fixtures/journal';

const navigation = vi.hoisted(() => ({ query: '', push: vi.fn() }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: navigation.push }),
  useSearchParams: () => new URLSearchParams(navigation.query),
}));
const root = `/api/patients/${ids.patient}/exercise-journal`;
const summary = {
  completedCount: 0,
  partialCount: 1,
  skippedCount: 0,
  lastPerformedOn: journalEntry.performedOn,
};
function available() {
  server.use(
    http.get(apiUrl(root), () => HttpResponse.json(pageOf([journalEntry]))),
    http.get(apiUrl(`${root}/summary`), () => HttpResponse.json(summary)),
    http.get(apiUrl(`${root}/${journalEntry.id}`), () => HttpResponse.json(journalEntry)),
  );
}

describe('therapist journal read-only view', () => {
  beforeEach(() => {
    navigation.query = '';
    useAuthStore.getState().setUser(ids.therapist, 'therapist@example.test', ['Therapist']);
    available();
  });

  it('shows backend summary, snapshot dose, actual dose and no editing controls', async () => {
    const totals = { completedCount: 10, partialCount: 1, skippedCount: 2, lastPerformedOn: '2026-11-17' };
    server.use(
      http.get(apiUrl(root), () =>
        HttpResponse.json({ ...pageOf([journalEntry]), totalCount: 13, totalPages: 2, hasNext: true }),
      ),
      http.get(apiUrl(`${root}/summary`), () => HttpResponse.json(totals)),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    const tiles = within(screen.getByRole('region', { name: 'Podsumowanie realizacji' }));
    expect(tiles.getByText('10', { exact: true })).toBeVisible();
    expect(tiles.getByText('17.11.2026')).toBeVisible();
    expect(screen.getByText('Serie: 3 · Powtórzenia: 10')).toBeVisible();
    expect(screen.getByText('Serie: 2 · Powtórzenia: 8')).toBeVisible();
    expect(screen.getByText('Nie oceniono')).toBeVisible();
    expect(
      screen.queryByRole('button', { name: /Edytuj|Usuń wpis|Zapisz wykonanie/ }),
    ).not.toBeInTheDocument();
  });

  it('shows loading until both list and summary are available', async () => {
    server.use(
      http.get(apiUrl(`${root}/summary`), async () => {
        await delay(100);
        return HttpResponse.json(summary);
      }),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    expect(screen.getByText('Pobieranie dziennika i podsumowania…')).toBeVisible();
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
    await screen.findByText(journalEntry.exercise.name);
  });

  it('distinguishes an empty accessible history from denied access', async () => {
    server.use(
      http.get(apiUrl(root), () => HttpResponse.json(pageOf([]))),
      http.get(apiUrl(`${root}/summary`), () =>
        HttpResponse.json({ completedCount: 0, partialCount: 0, skippedCount: 0, lastPerformedOn: null }),
      ),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText('Pacjent nie ma jeszcze zgłoszeń w dostępnej historii');
    expect(screen.getByText('Brak wykonań')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('does not invent a last performance date for skipped-only results', async () => {
    navigation.query = 'status=Skipped';
    server.use(
      http.get(apiUrl(root), () =>
        HttpResponse.json(
          pageOf([{ ...journalEntry, status: 'Skipped', actualSets: null, actualRepetitions: null }]),
        ),
      ),
      http.get(apiUrl(`${root}/summary`), () =>
        HttpResponse.json({ completedCount: 0, partialCount: 0, skippedCount: 1, lastPerformedOn: null }),
      ),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    expect(screen.getByText('Brak wykonań')).toBeVisible();
  });

  it('fetches details separately and displays historical instructions with explicit zero pain', async () => {
    const user = userEvent.setup();
    server.use(
      http.get(apiUrl(`${root}/${journalEntry.id}`), () =>
        HttpResponse.json({ ...journalEntry, painLevel: 0 }),
      ),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await user.click(await screen.findByRole('button', { name: 'Szczegóły wpisu' }));
    const dialog = within(screen.getByRole('dialog'));
    expect(await dialog.findByText('Historyczna instrukcja do testów.')).toBeVisible();
    expect(dialog.getByText(/0\/10 — brak bólu/)).toBeVisible();
    expect(dialog.getByText('Nie edytowano')).toBeVisible();
    expect(dialog.queryByRole('button', { name: /Zapisz|Edytuj|Usuń wpis/ })).not.toBeInTheDocument();
    await user.click(dialog.getByRole('button', { name: 'Zamknij szczegóły' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it.each(['list', 'summary', 'detail'] as const)(
    'clears all payloads after a 404 from %s',
    async (source) => {
      const user = userEvent.setup();
      renderWithQuery(<TherapistJournal patientId={ids.patient} />);
      await screen.findByText(journalEntry.exercise.name);
      const url =
        source === 'list' ? root : source === 'summary' ? `${root}/summary` : `${root}/${journalEntry.id}`;
      server.use(
        http.get(apiUrl(url), () =>
          HttpResponse.json({ detail: 'PRIVATE_RESOURCE_DETAIL' }, { status: 404 }),
        ),
      );
      if (source === 'detail') await user.click(screen.getByRole('button', { name: 'Szczegóły wpisu' }));
      else await user.click(screen.getByRole('button', { name: 'Odśwież dziennik' }));
      await screen.findByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa' });
      expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
      expect(screen.queryByRole('region', { name: 'Podsumowanie realizacji' })).not.toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByRole('alert')).not.toHaveTextContent('PRIVATE_RESOURCE_DETAIL');
    },
  );

  it('does not restore access when the URL filters change; explicit retry reads both afresh', async () => {
    let reads = 0;
    server.use(
      http.get(apiUrl(root), () => {
        reads++;
        return HttpResponse.json({}, { status: 404 });
      }),
    );
    const user = userEvent.setup();
    const { rerender } = renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByRole('alert');
    const before = reads;
    available();
    navigation.query = 'status=Partial';
    rerender(<TherapistJournal patientId={ids.patient} />);
    expect(screen.getByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa' })).toBeVisible();
    expect(reads).toBe(before);
    expect(screen.queryByLabelText('Status realizacji')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa' }));
    expect(await screen.findByText(journalEntry.exercise.name)).toBeVisible();
  });

  it('allows retrying a transient error without showing an old summary', async () => {
    const user = userEvent.setup();
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    server.use(http.get(apiUrl(`${root}/summary`), () => HttpResponse.json({}, { status: 500 })));
    await user.click(screen.getByRole('button', { name: 'Odśwież dziennik' }));
    await screen.findByRole('alert');
    expect(screen.queryByRole('region', { name: 'Podsumowanie realizacji' })).not.toBeInTheDocument();
    available();
    await user.click(screen.getByRole('button', { name: 'Pobierz ponownie' }));
    await screen.findByText(journalEntry.exercise.name);
  });

  it('clears the previous patient immediately when switching cards', async () => {
    const other = '99999999-9999-4999-8999-999999999999';
    server.use(
      http.get(apiUrl(`/api/patients/${other}/exercise-journal`), async () => {
        await delay(80);
        return HttpResponse.json(
          pageOf([
            {
              ...journalEntry,
              patientId: other,
              exercise: { ...journalEntry.exercise, name: 'OTHER_PATIENT_EXERCISE' },
            },
          ]),
        );
      }),
      http.get(apiUrl(`/api/patients/${other}/exercise-journal/summary`), () => HttpResponse.json(summary)),
    );
    const { rerender } = renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    rerender(<TherapistJournal patientId={other} />);
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Podsumowanie realizacji' })).not.toBeInTheDocument();
    await screen.findByText('OTHER_PATIENT_EXERCISE');
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
  });

  it('removes the view on logout', async () => {
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    act(() => useAuthStore.getState().logout());
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Podgląd wymaga zalogowanego konta terapeuty');
  });

  it('drops the previous account payload before a different therapist is checked', async () => {
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    server.use(http.get(apiUrl(root), () => HttpResponse.json({}, { status: 404 })));
    act(() => useAuthStore.getState().setUser('other-therapist', 'other@example.test', ['Therapist']));
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
    await screen.findByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa' });
    expect(screen.queryByRole('region', { name: 'Podsumowanie realizacji' })).not.toBeInTheDocument();
  });

  it('removes all data when the server returns 403 during a refresh', async () => {
    const user = userEvent.setup();
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    server.use(http.get(apiUrl(`${root}/summary`), () => new HttpResponse(null, { status: 403 })));
    await user.click(screen.getByRole('button', { name: 'Odśwież dziennik' }));
    await screen.findByRole('button', { name: 'Sprawdź dostęp i pobierz od nowa' });
    expect(screen.getByRole('alert')).toHaveTextContent('Nie masz uprawnień');
    expect(screen.queryByText(journalEntry.exercise.name)).not.toBeInTheDocument();
  });

  it('shows a distinct empty-filter message', async () => {
    navigation.query = 'from=2099-01-01';
    server.use(
      http.get(apiUrl(root), () => HttpResponse.json(pageOf([]))),
      http.get(apiUrl(`${root}/summary`), () =>
        HttpResponse.json({ completedCount: 0, partialCount: 0, skippedCount: 0, lastPerformedOn: null }),
      ),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText('Brak wpisów dla wybranych filtrów');
  });

  it('does not request data for a Patient-only role', () => {
    useAuthStore.getState().setUser(ids.patient, 'patient@example.test', ['Patient']);
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    expect(screen.getByRole('alert')).toBeVisible();
    expect(screen.queryByText('Pobieranie dziennika i podsumowania…')).not.toBeInTheDocument();
  });

  it('does not send invalid filters to the API', () => {
    navigation.query = 'from=2026-11-18&to=2026-11-17';
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Data początkowa');
    expect(screen.queryByText('Pobieranie dziennika i podsumowania…')).not.toBeInTheDocument();
  });

  it('applies filters explicitly and resets pagination', async () => {
    const user = userEvent.setup();
    navigation.query = 'page=3';
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    await user.selectOptions(screen.getByLabelText('Status realizacji'), 'Partial');
    expect(navigation.push).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Zastosuj' }));
    expect(navigation.push).toHaveBeenCalledWith(`/patients/${ids.patient}/exercise-journal?status=Partial`, {
      scroll: true,
    });
  });

  it('retains filters when changing pages and when selecting a snapshot position', async () => {
    const user = userEvent.setup();
    navigation.query = `therapyPlanId=${ids.plan}&status=Partial`;
    server.use(
      http.get(apiUrl(root), () => HttpResponse.json({ ...pageOf([journalEntry], 1, 2), totalCount: 11 })),
      http.get(apiUrl(`${root}/summary`), () => HttpResponse.json({ ...summary, partialCount: 11 })),
    );
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await user.click(await screen.findByRole('button', { name: 'Następna' }));
    const href = new URL(navigation.push.mock.calls[0][0], 'http://localhost');
    expect(Object.fromEntries(href.searchParams)).toEqual({
      therapyPlanId: ids.plan,
      status: 'Partial',
      page: '2',
    });
    await user.click(screen.getByRole('button', { name: 'Tylko ta pozycja' }));
    const position = new URL(navigation.push.mock.calls[1][0], 'http://localhost');
    expect(position.searchParams.get('planItemId')).toBe(ids.item);
    expect(position.searchParams.has('page')).toBe(false);
  });

  it('hides incoherent counters instead of presenting a misleading summary', async () => {
    server.use(http.get(apiUrl(`${root}/summary`), () => HttpResponse.json({ ...summary, partialCount: 9 })));
    renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(journalEntry.exercise.name);
    expect(screen.getByRole('button', { name: 'Odśwież listę i podsumowanie' })).toBeVisible();
    expect(screen.queryByRole('region', { name: 'Podsumowanie realizacji' })).not.toBeInTheDocument();
  });

  it('renders comments as text and never persists journal payloads', async () => {
    const text = '<img src=x onerror="alert(1)"> CLINICAL_JOURNAL_SENTINEL';
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([{ ...journalEntry, comment: text }]))));
    const { container } = renderWithQuery(<TherapistJournal patientId={ids.patient} />);
    await screen.findByText(text);
    expect(container.querySelector('img')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('CLINICAL_JOURNAL_SENTINEL');
    expect(JSON.stringify(sessionStorage)).not.toContain('CLINICAL_JOURNAL_SENTINEL');
  });
});
