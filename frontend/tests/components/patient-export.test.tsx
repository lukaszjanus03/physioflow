import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PatientExportDialog from '@/components/patients/PatientExportDialog';
import { patientExportApi } from '@/lib/patient-export-api';
import { useAuthStore } from '@/stores/auth-store';
import { ids } from '../fixtures/sharing';
import { exportEntry, exportPage, exportPatient } from '../fixtures/patient-export';

const renderer = vi.hoisted(() => ({ download: vi.fn(async () => {}) }));
vi.mock('@/components/patients/PatientCardPdf', () => ({ downloadPatientCardPdf: renderer.download }));
beforeEach(() => {
  useAuthStore.getState().setUser(ids.therapist, 'therapist@example.test', ['Therapist']);
  vi.spyOn(patientExportApi, 'patient').mockResolvedValue(exportPatient);
  vi.spyOn(patientExportApi, 'page').mockResolvedValue(exportPage([exportEntry()]));
});
async function open() {
  const view = render(<PatientExportDialog patientId={ids.patient} />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Eksport PDF' }));
  return { ...view, user };
}
describe('patient PDF export configuration', () => {
  it('does not fetch or generate simply by opening or closing the dialog', async () => {
    const { user } = await open();
    await user.click(screen.getByRole('button', { name: 'Zamknij' }));
    expect(patientExportApi.patient).not.toHaveBeenCalled();
    expect(patientExportApi.page).not.toHaveBeenCalled();
    expect(renderer.download).not.toHaveBeenCalled();
  });
  it('keeps the original card-only export and requires an explicit download', async () => {
    const { user } = await open();
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    await screen.findByRole('region', { name: 'Podsumowanie eksportu' });
    expect(renderer.download).not.toHaveBeenCalled();
    expect(patientExportApi.page).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Pobierz PDF' }));
    await waitFor(() => expect(renderer.download).toHaveBeenCalledTimes(1));
    expect(renderer.download.mock.calls[0]).toEqual([
      exportPatient,
      expect.objectContaining({ documentation: undefined }),
    ]);
  });
  it('prepares documentation separately and drops the preview when options change', async () => {
    const { user } = await open();
    await user.click(screen.getByLabelText(/Dołącz dokumentację wizyt/));
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    expect(await screen.findByText(/dokumentacja: 1 wpisów/)).toBeVisible();
    await user.click(screen.getByLabelText(/Dołącz dokumentację wizyt/));
    expect(screen.queryByRole('region', { name: 'Podsumowanie eksportu' })).not.toBeInTheDocument();
  });
  it('warns about an empty range before generating', async () => {
    vi.mocked(patientExportApi.page).mockResolvedValue(exportPage([]));
    const { user } = await open();
    await user.click(screen.getByLabelText(/Dołącz dokumentację wizyt/));
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Brak zatwierdzonych wpisów');
    expect(renderer.download).not.toHaveBeenCalled();
  });
  it('blocks invalid date ranges before any request', async () => {
    const { user } = await open();
    await user.click(screen.getByLabelText(/Dołącz dokumentację wizyt/));
    await user.type(screen.getByLabelText('Data wizyty od'), '2026-11-02');
    await user.type(screen.getByLabelText('Data wizyty do'), '2026-11-01');
    expect(screen.getByRole('alert')).toHaveTextContent('późniejsza');
    expect(screen.getByRole('button', { name: 'Przygotuj eksport' })).toBeDisabled();
    expect(patientExportApi.page).not.toHaveBeenCalled();
  });
  it('aborts when closed and ignores a late response', async () => {
    let finish!: (value: typeof exportPatient) => void;
    vi.mocked(patientExportApi.patient).mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const { user } = await open();
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    const signal = vi.mocked(patientExportApi.patient).mock.calls[0][1];
    await user.click(screen.getByRole('button', { name: 'Anuluj eksport' }));
    expect(signal.aborted).toBe(true);
    await act(async () => finish(exportPatient));
    expect(renderer.download).not.toHaveBeenCalled();
  });
  it('does not generate a partial document after the second page fails', async () => {
    vi.mocked(patientExportApi.page)
      .mockResolvedValueOnce(
        exportPage(
          Array.from({ length: 100 }, (_, i) => exportEntry(i + 1)),
          1,
          105,
        ),
      )
      .mockRejectedValueOnce(new Error('SECRET'));
    const { user } = await open();
    await user.click(screen.getByLabelText(/Dołącz dokumentację wizyt/));
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    expect(await screen.findByRole('alert')).not.toHaveTextContent('SECRET');
    expect(renderer.download).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Pobierz PDF' })).not.toBeInTheDocument();
  });
  it('clears prepared data and blocks download after access is denied', async () => {
    const { user } = await open();
    await user.click(screen.getByRole('button', { name: 'Przygotuj eksport' }));
    await screen.findByRole('button', { name: 'Pobierz PDF' });
    vi.mocked(patientExportApi.patient).mockRejectedValue({ isAxiosError: true, response: { status: 404 } });
    await user.click(screen.getByRole('button', { name: 'Pobierz PDF' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Brak dostępu');
    expect(renderer.download).not.toHaveBeenCalled();
    expect(screen.queryByRole('region', { name: 'Podsumowanie eksportu' })).not.toBeInTheDocument();
  });
});
