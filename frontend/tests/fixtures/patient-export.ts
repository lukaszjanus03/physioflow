import type { PatientDetail } from '@/types/api';
import type { ApprovedExportRecord } from '@/lib/patient-export-api';
import { ids } from './sharing';

export const exportPatient: PatientDetail = {
  id: ids.patient,
  firstName: 'Anna',
  lastName: 'Ćwiczebna',
  pesel: null,
  dateOfBirth: '1990-03-12',
  email: 'patient@example.test',
  phoneNumber: null,
  address: 'Adres syntetyczny',
  medicalHistory: 'Testowa historia medyczna.',
  medications: 'Brak leków w danych testowych.',
  generalNotes: 'Dane fikcyjne do testowania eksportu.',
  isArchived: false,
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: null,
  isLinkedToAccount: true,
};
export function exportEntry(number = 1, overrides: Partial<ApprovedExportRecord> = {}): ApprovedExportRecord {
  return {
    id: `aaaa0000-0000-4000-8000-${String(number).padStart(12, '0')}`,
    appointmentId: 'bbbb0000-0000-4000-8000-000000000001',
    appointmentStartsAt: '2026-10-20T23:30:00Z',
    versionNumber: 2,
    approvedAt: '2026-11-18T10:02:00Z',
    content: {
      interview: 'ZATWIERDZONY WYWIAD - dane testowe.',
      functionalAssessment: 'ZATWIERDZONA OCENA - dane testowe.',
      performedActions: 'ZATWIERDZONE DZIAŁANIA - dane testowe.',
      recommendations: 'ZATWIERDZONE ZALECENIA - dane testowe.',
      patientSummary: 'OSOBNE PODSUMOWANIE - dane testowe.',
    },
    ...overrides,
  };
}
export function exportPage(items: ApprovedExportRecord[], pageNumber = 1, totalCount = items.length) {
  return {
    items,
    pageNumber,
    totalCount,
    pageSize: 100,
    totalPages: Math.ceil(totalCount / 100),
    hasPrevious: pageNumber > 1,
    hasNext: pageNumber * 100 < totalCount,
  };
}
