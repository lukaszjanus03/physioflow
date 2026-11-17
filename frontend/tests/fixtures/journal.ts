import type { JournalEntry } from '@/lib/exercise-journal-api';
import type { JournalTarget } from '@/components/patient-journal/JournalEditor';
import { ids } from './sharing';

export const journalTarget: JournalTarget = {
  therapyPlanId: ids.plan,
  planVersionNumber: 1,
  planItemId: ids.item,
  publishedAt: '2020-01-01T09:00:00Z',
  planName: 'Historyczny plan testowy',
  exercise: {
    name: 'Testowe ćwiczenie w siadzie',
    instructions: 'Historyczna instrukcja do testów.',
    order: 0,
    sets: 3,
    repetitions: 10,
    durationSeconds: null,
    frequency: 'Według planu',
    notes: 'Publiczne wskazówki testowe',
  },
};
export const journalEntry: JournalEntry = {
  id: '77777777-7777-4777-8777-777777777777',
  patientId: ids.patient,
  therapyPlanId: ids.plan,
  therapyPlanVersionId: '88888888-8888-4888-8888-888888888888',
  planVersionNumber: 1,
  planItemId: ids.item,
  publishedAt: journalTarget.publishedAt,
  planName: journalTarget.planName,
  exercise: journalTarget.exercise,
  performedOn: '2020-01-02',
  status: 'Partial',
  actualSets: 2,
  actualRepetitions: 8,
  actualDurationSeconds: null,
  painLevel: null,
  comment: 'Pierwotny komentarz',
  createdAt: '2020-01-02T10:00:00Z',
  updatedAt: null,
  version: 'journal-token-1',
};
