import { isAxiosError } from 'axios';
import { apiClient } from '@/lib/api-client';
import type { PaginatedList } from '@/types/api';

export type Exercise = {
  id: string;
  name: string;
  instructions: string | null;
  category: string | null;
  notes: string | null;
  isArchived: boolean;
  version: string;
};

export type ExerciseBody = {
  name: string;
  instructions: string;
  category: string;
  notes: string;
};

export type PlanStatus = 'Draft' | 'Active' | 'Completed' | 'Archived';

export const planLabels: Record<PlanStatus, string> = {
  Draft: 'Szkic',
  Active: 'Aktywny',
  Completed: 'Zakończony',
  Archived: 'Archiwalny',
};

export type PlanItem = {
  id: string;
  exerciseId: string;
  order: number;
  name: string;
  instructions: string | null;
  sets: number | null;
  repetitions: number | null;
  durationSeconds: number | null;
  frequency: string | null;
  notes: string | null;
};

export type PlanContent = {
  name: string | null;
  goal: string | null;
  startDate: string | null;
  endDate: string | null;
  generalRecommendations: string | null;
  exercises: PlanItem[];
};

export type TherapyPlan = {
  id: string;
  patientId: string;
  status: PlanStatus;
  currentVersion: number;
  version: string;
  createdAt: string;
  updatedAt: string | null;
  content: PlanContent;
};

export type PlanListItem = Omit<
  TherapyPlan,
  'patientId' | 'version' | 'content'
> & Pick<PlanContent, 'name' | 'startDate' | 'endDate'>;

export type PlanVersion = {
  versionNumber: number;
  authorUserId: string;
  activatedAt: string;
  correctionReason: string | null;
  content: PlanContent;
};

export type PlanBody = {
  name: string;
  goal: string;
  startDate: string | null;
  endDate: string | null;
  generalRecommendations: string;
  exercises: {
    id: string | null;
    exerciseId: string;
    sets: number | null;
    repetitions: number | null;
    durationSeconds: number | null;
    frequency: string;
    notes: string;
  }[];
};

export const therapyOptions = {
  staleTime: 0,
  gcTime: 0,
  retry: false,
  refetchOnWindowFocus: false,
  refetchOnReconnect: false,
} as const;

export const therapyApi = {
  exercises: (
    filters: {
      search: string;
      category: string;
      archived: string;
      page: number;
    },
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PaginatedList<Exercise>>('/api/exercises', {
        params: {
          search: filters.search,
          category: filters.category,
          archived: filters.archived,
          pageNumber: filters.page,
          pageSize: 8,
        },
        signal,
        timeout: 15000,
      })
      .then((response) => response.data),

  exercise: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<Exercise>(`/api/exercises/${id}`, {
        signal,
        timeout: 15000,
      })
      .then((response) => response.data),

  saveExercise: (base: Exercise | null, body: ExerciseBody) =>
    (
      base
        ? apiClient.put<Exercise>(
            `/api/exercises/${base.id}`,
            { ...body, version: base.version },
            { timeout: 15000 },
          )
        : apiClient.post<Exercise>(
            '/api/exercises',
            body,
            { timeout: 15000 },
          )
    ).then((response) => response.data),

  archiveExercise: (base: Exercise) =>
    apiClient
      .post<Exercise>(
        `/api/exercises/${base.id}/${base.isArchived ? 'restore' : 'archive'}`,
        { version: base.version },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  plans: (
    patientId: string,
    status: string,
    page: number,
    signal?: AbortSignal,
  ) =>
    apiClient
      .get<PaginatedList<PlanListItem>>(
        `/api/patients/${patientId}/therapy-plans`,
        {
          params: {
            ...(status ? { status } : {}),
            pageNumber: page,
            pageSize: 10,
          },
          signal,
          timeout: 15000,
        },
      )
      .then((response) => response.data),

  createPlan: (patientId: string) =>
    apiClient
      .post<TherapyPlan>(
        `/api/patients/${patientId}/therapy-plans`,
        { exercises: [] },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  plan: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<TherapyPlan>(`/api/therapy-plans/${id}`, {
        signal,
        timeout: 15000,
      })
      .then((response) => response.data),

  writePlan: (
    base: TherapyPlan,
    body: PlanBody,
    reason?: string,
  ) =>
    (
      reason === undefined
        ? apiClient.put<TherapyPlan>(
            `/api/therapy-plans/${base.id}`,
            { ...body, version: base.version },
            { timeout: 15000 },
          )
        : apiClient.post<TherapyPlan>(
            `/api/therapy-plans/${base.id}/corrections`,
            {
              ...body,
              version: base.version,
              correctionReason: reason,
            },
            { timeout: 15000 },
          )
    ).then((response) => response.data),

  transition: (
    base: TherapyPlan,
    action: 'activate' | 'complete' | 'archive',
  ) =>
    apiClient
      .post<TherapyPlan>(
        `/api/therapy-plans/${base.id}/${action}`,
        { version: base.version },
        { timeout: 15000 },
      )
      .then((response) => response.data),

  versions: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<PlanVersion[]>(
        `/api/therapy-plans/${id}/versions`,
        { signal, timeout: 15000 },
      )
      .then((response) => response.data),
};

export function therapyStatus(error: unknown) {
  return isAxiosError(error)
    ? error.response?.status
    : undefined;
}

export function therapyError(error: unknown) {
  const code = isAxiosError(error)
    ? (
        error.response?.data as
          | { code?: string }
          | undefined
      )?.code
    : undefined;

  const messages: Record<string, string> = {
    concurrent_modification:
      'Dane zostały zmienione. Twoja treść pozostaje lokalnie. Pobierz aktualny stan i porównaj zmiany.',
    exercise_archived:
      'Nowa pozycja wskazuje zarchiwizowane ćwiczenie. Usuń ją lub przywróć ćwiczenie w bibliotece.',
    invalid_plan_transition:
      'Status planu nie pozwala na tę operację. Pobierz aktualne dane.',
    patient_archived:
      'Karta pacjenta jest archiwalna. Zmiany planów są zablokowane.',
  };

  if (code && messages[code]) return messages[code];

  switch (therapyStatus(error)) {
    case 400:
      return 'Sprawdź wymagane pola, limity, daty i parametry ćwiczeń.';
    case 401:
      return 'Sesja wygasła. Zaloguj się ponownie.';
    case 403:
      return 'Ta funkcja jest dostępna wyłącznie uprawnionemu terapeucie.';
    case 404:
      return 'Nie znaleziono zasobu dostępnego dla Twojego konta. Sprawdź także, czy pozycje planu nie zostały zmienione.';
    case 409:
      return 'Stan danych się zmienił. Pobierz aktualne dane przed kolejną próbą.';
    default:
      return 'Operacja nie została potwierdzona. Sprawdź połączenie i aktualny stan przed ponownym zapisem.';
  }
}

export function therapyFieldErrors(error: unknown) {
  if (!isAxiosError(error)) return {};

  const errors = (
    error.response?.data as
      | { errors?: Record<string, unknown> }
      | undefined
  )?.errors;

  return Object.fromEntries(
    Object.keys(errors ?? {}).map((key) => [
      key,
      'Serwer odrzucił to pole. Sprawdź wartość i limit.',
    ]),
  );
}

export const therapyId = (value: string) =>
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);

export const therapyDate = (value: string | null) =>
  value
    ? value.split('-').reverse().join('.')
    : 'Nie ustalono';