import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { PatientCardPdf, downloadPatientCardPdf } from '@/components/patients/PatientCardPdf';
import { exportEntry, exportPatient } from '../fixtures/patient-export';

const engine = vi.hoisted(() => ({
  blob: vi.fn(async () => new Blob(['synthetic-pdf'], { type: 'application/pdf' })),
  pdf: vi.fn(),
}));
vi.mock('@react-pdf/renderer', () => {
  const Box = ({ children }: { children?: ReactNode }) => <div>{children}</div>;
  return {
    Document: Box,
    Page: Box,
    View: Box,
    Text: ({
      children,
      render: footer,
    }: {
      children?: ReactNode;
      render?: (value: { pageNumber: number; totalPages: number }) => string;
    }) => <span>{footer ? footer({ pageNumber: 1, totalPages: 1 }) : children}</span>,
    StyleSheet: { create: (styles: unknown) => styles },
    Font: { register: vi.fn() },
    pdf: (document: unknown) => {
      engine.pdf(document);
      return { toBlob: engine.blob };
    },
  };
});
describe('patient PDF content and local download', () => {
  it('preserves original card sections without adding documentation by default', () => {
    render(<PatientCardPdf patient={exportPatient} generatedAt={new Date('2026-11-18T12:00:00Z')} />);
    expect(screen.getByText('Historia medyczna', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('Notatki ogólne', { exact: true })).toBeInTheDocument();
    expect(screen.queryByText('Zatwierdzona dokumentacja wizyt', { exact: true })).not.toBeInTheDocument();
  });
  it('renders separate clinical sections, approved version and Warsaw appointment date', () => {
    const entry = exportEntry();
    render(
      <PatientCardPdf
        patient={exportPatient}
        generatedAt={new Date()}
        documentation={{ from: '2026-10-01', to: '', entries: [entry] }}
      />,
    );
    for (const label of [
      'Wywiad',
      'Obserwacje i ocena funkcjonalna',
      'Wykonane działania',
      'Zalecenia',
      'Podsumowanie dla pacjenta (osobna treść)',
    ])
      expect(screen.getByText(label, { exact: true })).toBeInTheDocument();
    expect(screen.getByText(/Wizyta: 21\.10\.2026/)).toBeInTheDocument();
    expect(screen.getByText(/WERSJA 2/)).toBeInTheDocument();
    expect(screen.getByText(/Zatwierdzono: 18\.11\.2026/)).toBeInTheDocument();
  });
  it('keeps legacy records undated and omits an empty patient summary', () => {
    const entry = exportEntry(1, {
      appointmentStartsAt: null,
      appointmentId: null,
      content: { ...exportEntry().content, patientSummary: null },
    });
    render(
      <PatientCardPdf
        patient={exportPatient}
        generatedAt={new Date()}
        documentation={{ from: '', to: '', entries: [entry] }}
      />,
    );
    expect(screen.getByText('Wpis bez przypisanej wizyty')).toBeInTheDocument();
    expect(screen.queryByText('Podsumowanie dla pacjenta (osobna treść)')).not.toBeInTheDocument();
  });
  it('renders HTML-like content as text and keeps the full long text', () => {
    const long = 'Długi tekst testowy. '.repeat(500) + 'END_OF_CLINICAL_SECTION';
    const entry = exportEntry(1, {
      content: { ...exportEntry().content, interview: '<script>NO_SCRIPT</script>', recommendations: long },
    });
    render(
      <PatientCardPdf
        patient={exportPatient}
        generatedAt={new Date()}
        documentation={{ from: '', to: '', entries: [entry] }}
      />,
    );
    expect(screen.getByText('<script>NO_SCRIPT</script>')).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
    expect(document.body.textContent).toContain('END_OF_CLINICAL_SECTION');
  });
  it('downloads a blob and revokes its temporary URL', async () => {
    vi.useFakeTimers();
    const create = vi.fn(() => 'blob:synthetic');
    const revoke = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: create, revokeObjectURL: revoke }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    await downloadPatientCardPdf(exportPatient);
    expect(click).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledTimes(1);
    expect(document.querySelector('a[download]')).toBeNull();
    await vi.advanceTimersByTimeAsync(60000);
    expect(revoke).toHaveBeenCalledWith('blob:synthetic');
  });
  it('never downloads a generated blob after cancellation or loss of access', async () => {
    const create = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: create, revokeObjectURL: vi.fn() }));
    const controller = new AbortController();
    engine.blob.mockImplementationOnce(async () => {
      controller.abort();
      return new Blob();
    });
    await expect(downloadPatientCardPdf(exportPatient, { signal: controller.signal })).rejects.toBeDefined();
    await expect(downloadPatientCardPdf(exportPatient, { canDownload: () => false })).rejects.toThrow(
      'export-access-changed',
    );
    expect(create).not.toHaveBeenCalled();
  });
});
