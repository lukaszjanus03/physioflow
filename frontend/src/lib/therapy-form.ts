import type {
  ExerciseBody,
  PlanBody,
  PlanContent,
  TherapyPlan,
} from '@/lib/therapy-api';

export type ItemForm = {
  mode: '' | 'reps' | 'time';
  key: string;
  id: string | null;
  exerciseId: string;
  name: string;
  instructions: string;
  sets: string;
  repetitions: string;
  durationSeconds: string;
  frequency: string;
  notes: string;
};

export type PlanForm = {
  name: string;
  goal: string;
  startDate: string;
  endDate: string;
  generalRecommendations: string;
  exercises: ItemForm[];
};

export function planForm(content: PlanContent): PlanForm {
  return {
    name: content.name ?? '',
    goal: content.goal ?? '',
    startDate: content.startDate ?? '',
    endDate: content.endDate ?? '',
    generalRecommendations:
      content.generalRecommendations ?? '',
    exercises: content.exercises.map((item) => ({
      mode:
        item.durationSeconds !== null
          ? 'time'
          : item.repetitions !== null
            ? 'reps'
            : '',
      key: item.id,
      id: item.id,
      exerciseId: item.exerciseId,
      name: item.name,
      instructions: item.instructions ?? '',
      sets: item.sets?.toString() ?? '',
      repetitions: item.repetitions?.toString() ?? '',
      durationSeconds:
        item.durationSeconds?.toString() ?? '',
      frequency: item.frequency ?? '',
      notes: item.notes ?? '',
    })),
  };
}

const numeric = (value: string) =>
  value === '' ? null : Number(value);

export function planPayload(form: PlanForm): PlanBody {
  return {
    name: form.name,
    goal: form.goal,
    startDate: form.startDate || null,
    endDate: form.endDate || null,
    generalRecommendations:
      form.generalRecommendations,
    exercises: form.exercises.map((item) => ({
      id: item.id,
      exerciseId: item.exerciseId,
      sets: numeric(item.sets),
      repetitions: numeric(item.repetitions),
      durationSeconds: numeric(item.durationSeconds),
      frequency: item.frequency,
      notes: item.notes,
    })),
  };
}

function validDate(value: string) {
  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
    value.startsWith('0000-') ||
    ['0001-01-01', '9999-12-31'].includes(value)
  ) {
    return false;
  }

  const date = new Date(`${value}T00:00:00Z`);

  return (
    !Number.isNaN(date.getTime()) &&
    date.toISOString().slice(0, 10) === value
  );
}

export function exerciseErrors(body: ExerciseBody) {
  const errors: Record<string, string> = {};

  for (const [key, max] of [
    ['name', 200],
    ['instructions', 10000],
    ['category', 100],
    ['notes', 2000],
  ] as const) {
    if (body[key].length > max) {
      errors[key] = `Maksymalnie ${max} znaków.`;
    } else if (key !== 'notes' && !body[key].trim()) {
      errors[key] = 'Uzupełnij to pole.';
    }
  }

  return errors;
}

export function planErrors(
  form: PlanForm,
  base: TherapyPlan,
  complete: boolean,
  reason?: string,
) {
  const errors: Record<string, string> = {};

  for (const [key, max] of [
    ['name', 200],
    ['goal', 10000],
    ['generalRecommendations', 10000],
  ] as const) {
    if (form[key].length > max) {
      errors[key] = `Maksymalnie ${max} znaków.`;
    }
  }

  if (complete && !form.name.trim()) {
    errors.name = 'Podaj nazwę planu.';
  }

  for (const key of ['startDate', 'endDate'] as const) {
    if (
      (form[key] && !validDate(form[key])) ||
      (complete && !form[key])
    ) {
      errors[key] = 'Podaj poprawną datę.';
    }
  }

  if (
    form.startDate &&
    form.endDate &&
    form.endDate < form.startDate
  ) {
    errors.endDate =
      'Koniec nie może poprzedzać początku.';
  }

  if (
    form.exercises.length > 100 ||
    (complete && !form.exercises.length)
  ) {
    errors.exercises =
      'Plan wymaga od 1 do 100 ćwiczeń przy aktywacji.';
  }

  const ids = new Set<string>();

  form.exercises.forEach((item, index) => {
    const prefix = `exercises[${index}]`;

    if (
      item.id &&
      (ids.has(item.id) ||
        !base.content.exercises.some(
          (old) =>
            old.id === item.id &&
            old.exerciseId === item.exerciseId,
        ))
    ) {
      errors[`${prefix}.id`] =
        'Pozycja nie należy do aktualnej wersji. Usuń ją i ponownie wybierz ćwiczenie.';
    }

    if (item.id) ids.add(item.id);

    for (const [key, max] of [
      ['sets', 100],
      ['repetitions', 1000],
      ['durationSeconds', 86400],
    ] as const) {
      const value = item[key];

      if (
        value !== '' &&
        (!Number.isInteger(Number(value)) ||
          Number(value) < 1 ||
          Number(value) > max)
      ) {
        errors[`${prefix}.${key}`] =
          `Liczba całkowita od 1 do ${max}.`;
      }
    }

    if (complete && !item.sets) {
      errors[`${prefix}.sets`] =
        'Podaj liczbę serii.';
    }

    if (
      (item.repetitions !== '' &&
        item.durationSeconds !== '') ||
      (complete &&
        item.repetitions === '' &&
        item.durationSeconds === '')
    ) {
      errors[`${prefix}.repetitions`] =
        'Wybierz powtórzenia albo czas, nigdy oba.';
    }

    if (
      item.frequency.length > 200 ||
      (complete && !item.frequency.trim())
    ) {
      errors[`${prefix}.frequency`] =
        'Podaj częstotliwość, maksymalnie 200 znaków.';
    }

    if (item.notes.length > 2000) {
      errors[`${prefix}.notes`] =
        'Maksymalnie 2000 znaków.';
    }

    if (
      complete &&
      (!item.name.trim() || !item.instructions.trim())
    ) {
      errors[`${prefix}.id`] =
        'Brak nazwy lub instrukcji w kopii ćwiczenia. Zastąp pozycję poprawnym ćwiczeniem.';
    }
  });

  if (
    reason !== undefined &&
    (!reason.trim() || reason.length > 1000)
  ) {
    errors.correctionReason =
      'Podaj uzasadnienie od 1 do 1000 znaków.';
  }

  return errors;
}