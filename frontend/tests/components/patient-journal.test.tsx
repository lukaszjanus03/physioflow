import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import PatientJournal from '@/components/patient-journal/PatientJournal';
import { renderWithQuery } from '../render';
import { apiUrl, server } from '../mocks/server';
import { journalEntry } from '../fixtures/journal';
import { ids, pageOf } from '../fixtures/sharing';

const navigation = vi.hoisted(() => ({ query: '', push: vi.fn() }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: navigation.push }),
  useSearchParams: () => new URLSearchParams(navigation.query),
}));
const root = '/api/patient-portal/exercise-journal';

describe('patient journal history', () => {
  beforeEach(() => {
    navigation.query = '';
  });

  it('explains that missing entries are not skipped exercises', async () => {
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([]))));
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    expect(await screen.findByText('Twój dziennik czeka na pierwszy wpis')).toBeVisible();
    expect(screen.getByText(/Brak wpisu nie oznacza pominięcia/)).toBeVisible();
  });

  it('reads historical snapshots without depending on current plan access', async () => {
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([journalEntry]))));
    const user = userEvent.setup();
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    expect(await screen.findByText(journalEntry.exercise.name)).toBeVisible();
    expect(screen.getByText('Nie oceniono')).toBeVisible();
    await user.click(screen.getByText('Zalecenia z zapisanej wersji planu'));
    expect(screen.getByText('Historyczna instrukcja do testów.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Edytuj wpis' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Usuń wpis' })).toBeEnabled();
  });

  it('distinguishes no matching filters from an empty journal', async () => {
    navigation.query = 'status=Skipped';
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([]))));
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    expect(await screen.findByText('Brak wpisów dla wybranych filtrów')).toBeVisible();
  });

  it('does not send invalid URL filters to the API', () => {
    navigation.query = 'from=2026-11-17&to=2026-11-16';
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Data początkowa nie może być późniejsza');
    expect(screen.queryByText('Pobieranie dziennika…')).not.toBeInTheDocument();
  });

  it('retains the historical plan filter when changing pages', async () => {
    navigation.query = `therapyPlanId=${ids.plan}&status=Partial`;
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([journalEntry], 1, 2))));
    const user = userEvent.setup();
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    await user.click(await screen.findByRole('button', { name: 'Następna' }));
    const [href, options] = navigation.push.mock.calls[0];
    expect(Object.fromEntries(new URL(href, 'http://localhost').searchParams)).toEqual({
      view: 'journal',
      page: '2',
      therapyPlanId: ids.plan,
      status: 'Partial',
    });
    expect(options).toEqual({ scroll: true });
  });

  it('applies status filters explicitly and resets the page', async () => {
    navigation.query = 'page=3';
    server.use(http.get(apiUrl(root), () => HttpResponse.json(pageOf([], 3, 3))));
    const user = userEvent.setup();
    renderWithQuery(<PatientJournal accountId={ids.patient} />);
    await screen.findByText('Brak wpisów na tej stronie');
    await user.selectOptions(screen.getByLabelText('Status realizacji'), 'Skipped');
    expect(navigation.push).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Zastosuj' }));
    expect(navigation.push).toHaveBeenCalledWith('/portal?view=journal&page=1&status=Skipped', {
      scroll: true,
    });
  });
});
