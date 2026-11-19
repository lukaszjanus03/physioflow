import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { PatientPainAnalytics, PracticeAnalytics } from '@/components/analytics/AnalyticsPanels';
import { analyticsApi, type DashboardStats } from '@/lib/analytics-api';

vi.mock('next/dynamic', () => ({ default: () => () => <div>Wykres testowy</div> }));

const period = { from: '2026-11-16', to: '2026-11-22', timeZone: 'Europe/Warsaw' };
const dashboard: DashboardStats = {
  ...period,
  activePatientCount: 12,
  appointments: { total: 0, scheduled: 0, completed: 0, cancelled: 0, noShow: 0 },
};

it('shows active patients and zero appointments for an empty range', async () => {
  vi.spyOn(analyticsApi, 'dashboard').mockResolvedValue(dashboard);
  render(<PracticeAnalytics />);
  expect(await screen.findByText('12')).toBeVisible();
  expect(screen.getAllByText('0')).toHaveLength(5);
  expect(screen.getByText('Brak wizyt w wybranym zakresie.')).toBeVisible();
});

it('validates dates before requesting data and ignores the prior response after range changes', async () => {
  let finish!: (value: DashboardStats) => void;
  const api = vi
    .spyOn(analyticsApi, 'dashboard')
    .mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    )
    .mockResolvedValue({ ...dashboard, activePatientCount: 19 });
  render(<PracticeAnalytics />);
  fireEvent.click(screen.getByRole('button', { name: 'Własne daty' }));
  fireEvent.change(screen.getByLabelText('Od'), { target: { value: '2026-11-20' } });
  fireEvent.change(screen.getByLabelText('Do'), { target: { value: '2026-11-19' } });
  expect(screen.getByRole('button', { name: 'Zastosuj zakres' })).toBeDisabled();
  expect(api).toHaveBeenCalledTimes(1);
  fireEvent.change(screen.getByLabelText('Od'), { target: { value: '2026-11-18' } });
  fireEvent.click(screen.getByRole('button', { name: 'Zastosuj zakres' }));
  expect(await screen.findByText('19')).toBeVisible();
  expect(api.mock.calls[0][1].aborted).toBe(true);
  expect(api.mock.calls[1][0]).toEqual({ from: '2026-11-18', to: '2026-11-19' });
  await act(async () => finish(dashboard));
  expect(screen.queryByText('12')).not.toBeInTheDocument();
});

it('distinguishes missing ratings from zero and removes both chart and table on denial', async () => {
  const api = vi.spyOn(analyticsApi, 'pain').mockResolvedValue({
    ...period,
    points: [
      { date: '2026-11-16', averagePainLevel: null, ratingCount: 0 },
      { date: '2026-11-17', averagePainLevel: 0, ratingCount: 1 },
    ],
  });
  render(<PatientPainAnalytics patientId="card" />);
  await screen.findByText('Wykres testowy');
  fireEvent.click(screen.getByText('Tabela ocen bólu'));
  expect(within(screen.getByRole('row', { name: /16\.11\.2026/ })).getByText('Brak oceny')).toBeVisible();
  expect(within(screen.getByRole('row', { name: /17\.11\.2026/ })).getByText('0')).toBeVisible();
  api.mockRejectedValue({ isAxiosError: true, response: { status: 404 } });
  fireEvent.focus(window);
  expect(await screen.findByRole('alert')).toHaveTextContent('niedostępne');
  expect(screen.queryByText('Wykres testowy')).not.toBeInTheDocument();
  expect(screen.queryByText('Tabela ocen bólu')).not.toBeInTheDocument();
});

it('shows no chart when all ratings are missing', async () => {
  vi.spyOn(analyticsApi, 'pain').mockResolvedValue({
    ...period,
    points: [{ date: '2026-11-17', averagePainLevel: null, ratingCount: 0 }],
  });
  render(<PatientPainAnalytics patientId="card" />);
  expect(await screen.findByText('Brak ocen bólu w wybranym zakresie.')).toBeVisible();
  expect(screen.queryByText('Wykres testowy')).not.toBeInTheDocument();
});
