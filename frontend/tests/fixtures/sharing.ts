import type { MedicalRecord, RecordVersion } from '@/lib/medical-records-api';
import type { TherapyPlan, PlanVersion } from '@/lib/therapy-api';
import type { PatientPlan, PatientSummary } from '@/lib/patient-sharing-api';

export const ids = {
  plan: '11111111-1111-4111-8111-111111111111',
  record: '22222222-2222-4222-8222-222222222222',
  patient: '33333333-3333-4333-8333-333333333333',
  therapist: '44444444-4444-4444-8444-444444444444',
  item: '55555555-5555-4555-8555-555555555555',
};
export const publication = {
  versionNumber: 1,
  patientUserId: ids.patient,
  publishedAt: '2026-11-15T10:00:00Z',
  isAccessible: true,
};
export const plan: TherapyPlan = {
  id: ids.plan,
  patientId: ids.patient,
  status: 'Active',
  currentVersion: 2,
  version: 'edit-token-2',
  createdAt: '2026-11-14T10:00:00Z',
  updatedAt: null,
  publication: null,
  content: {
    name: 'Plan testowy - wersja 2',
    goal: 'Publiczny cel planu',
    startDate: '2026-11-15',
    endDate: null,
    generalRecommendations: 'Publiczne wskazowki',
    exercises: [],
  },
};
export const planVersions: PlanVersion[] = [2, 1].map((versionNumber) => ({
  versionNumber,
  authorUserId: ids.therapist,
  activatedAt: '2026-11-14T10:00:00Z',
  correctionReason: null,
  content: { ...plan.content, name: `Plan testowy - wersja ${versionNumber}` },
}));
export const record: MedicalRecord = {
  id: ids.record,
  patientId: ids.patient,
  therapistId: ids.therapist,
  appointmentId: '66666666-6666-4666-8666-666666666666',
  status: 'Approved',
  currentVersion: 2,
  version: 'record-edit-token-2',
  createdAt: '2026-11-14T10:00:00Z',
  updatedAt: null,
  publication: null,
  content: {
    interview: 'PRIVATE_INTERVIEW',
    functionalAssessment: 'PRIVATE_ASSESSMENT',
    performedActions: 'PRIVATE_ACTIONS',
    recommendations: 'PRIVATE_RECOMMENDATIONS',
    patientSummary: 'PUBLIC_SUMMARY_2',
  },
};
export const recordVersions: RecordVersion[] = [2, 1].map((versionNumber) => ({
  versionNumber,
  authorUserId: ids.therapist,
  recordedAt: '2026-11-14T10:00:00Z',
  correctionReason: 'PRIVATE_CORRECTION_REASON',
  content: { ...record.content, patientSummary: `PUBLIC_SUMMARY_${versionNumber}` },
}));
export const patientPlan: PatientPlan = {
  id: ids.plan,
  therapistId: ids.therapist,
  versionNumber: 1,
  publishedAt: publication.publishedAt,
  content: { ...plan.content, name: 'Plan testowy - opublikowana wersja 1', exercises: [] },
};
export const patientSummary: PatientSummary = {
  id: ids.record,
  therapistId: ids.therapist,
  appointmentId: record.appointmentId!,
  appointmentStartsAt: '2026-11-14T09:00:00Z',
  versionNumber: 1,
  publishedAt: publication.publishedAt,
  summary: 'PUBLIC_SUMMARY_1',
};
export function pageOf<T>(items: T[], pageNumber = 1, totalPages = 1) {
  return {
    items,
    pageNumber,
    pageSize: 10,
    totalCount: items.length,
    totalPages,
    hasPrevious: pageNumber > 1,
    hasNext: pageNumber < totalPages,
  };
}
