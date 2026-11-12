import { isAxiosError } from 'axios';

import { apiClient } from '@/lib/api-client';
import { getApiMessage } from '@/lib/portal-api';
import type {
  ApiProblem,
  TherapistOwnProfile,
} from '@/lib/portal-api';

export const marketplaceFields = [
  'experience',
  'education',
  'street',
  'buildingNumber',
  'postalCode',
  'publicPhoneNumber',
  'publicEmail',
  'organizationalInfo',
  'firstVisitPreparation',
] as const;

export type MarketplaceField =
  (typeof marketplaceFields)[number];

export type MarketplaceData = Record<
  MarketplaceField,
  string | null
> & {
  specializations: string[];
};

export type MarketplaceProfile = TherapistOwnProfile & {
  marketplace: MarketplaceData;
  photoUrl: string | null;
};

export type Specialization = {
  code: string;
  label: string;
};

export type ProfileValues = Record<MarketplaceField, string> & {
  firstName: string;
  lastName: string;
  city: string;
  practiceName: string;
  bio: string;
  isPublished: boolean;
  specializations: string[];
};

export type ProfileTextField = Exclude<
  keyof ProfileValues,
  'isPublished' | 'specializations'
>;

export const profileFields: {
  name: ProfileTextField;
  label: string;
  max: number;
  section:
    | 'basic'
    | 'professional'
    | 'address'
    | 'contact'
    | 'visit';
  multiline?: boolean;
  type?: 'email' | 'tel';
}[] = [
  {
    name: 'firstName',
    label: 'Imię',
    max: 100,
    section: 'basic',
  },
  {
    name: 'lastName',
    label: 'Nazwisko',
    max: 100,
    section: 'basic',
  },
  {
    name: 'practiceName',
    label: 'Nazwa gabinetu',
    max: 150,
    section: 'basic',
  },
  {
    name: 'city',
    label: 'Miejscowość',
    max: 100,
    section: 'basic',
  },
  {
    name: 'bio',
    label: 'Krótki opis zawodowy',
    max: 500,
    section: 'professional',
    multiline: true,
  },
  {
    name: 'experience',
    label: 'Doświadczenie',
    max: 2000,
    section: 'professional',
    multiline: true,
  },
  {
    name: 'education',
    label: 'Wykształcenie',
    max: 2000,
    section: 'professional',
    multiline: true,
  },
  {
    name: 'street',
    label: 'Ulica',
    max: 150,
    section: 'address',
  },
  {
    name: 'buildingNumber',
    label: 'Numer budynku / lokalu',
    max: 20,
    section: 'address',
  },
  {
    name: 'postalCode',
    label: 'Kod pocztowy',
    max: 6,
    section: 'address',
  },
  {
    name: 'publicPhoneNumber',
    label: 'Publiczny telefon gabinetu',
    max: 30,
    section: 'contact',
    type: 'tel',
  },
  {
    name: 'publicEmail',
    label: 'Publiczny e-mail gabinetu',
    max: 254,
    section: 'contact',
    type: 'email',
  },
  {
    name: 'organizationalInfo',
    label: 'Informacje organizacyjne',
    max: 2000,
    section: 'visit',
    multiline: true,
  },
  {
    name: 'firstVisitPreparation',
    label: 'Przygotowanie do pierwszej wizyty',
    max: 2000,
    section: 'visit',
    multiline: true,
  },
];

export const profileKeys = {
  own: (accountId: string) =>
    ['therapist-own-profile', accountId] as const,

  specializations: (accountId: string) =>
    ['therapist-specializations', accountId] as const,

  photo: (accountId: string, id: string) =>
    ['therapist-profile-photo', accountId, id] as const,
};

export function profileToValues(
  profile: MarketplaceProfile,
): ProfileValues {
  return {
    firstName: profile.firstName,
    lastName: profile.lastName,
    city: profile.city ?? '',
    practiceName: profile.practiceName ?? '',
    bio: profile.bio ?? '',
    isPublished: profile.isPublished,
    specializations: [...profile.marketplace.specializations],
    experience: profile.marketplace.experience ?? '',
    education: profile.marketplace.education ?? '',
    street: profile.marketplace.street ?? '',
    buildingNumber: profile.marketplace.buildingNumber ?? '',
    postalCode: profile.marketplace.postalCode ?? '',
    publicPhoneNumber:
      profile.marketplace.publicPhoneNumber ?? '',
    publicEmail: profile.marketplace.publicEmail ?? '',
    organizationalInfo:
      profile.marketplace.organizationalInfo ?? '',
    firstVisitPreparation:
      profile.marketplace.firstVisitPreparation ?? '',
  };
}

export function validateProfile(values: ProfileValues) {
  const errors: Partial<
    Record<keyof ProfileValues, string>
  > = {};

  for (const field of profileFields) {
    if (values[field.name].trim().length > field.max) {
      errors[field.name] =
        `Maksymalnie ${field.max} znaków.`;
    }
  }

  if (!values.firstName.trim()) {
    errors.firstName = 'Imię jest wymagane.';
  }

  if (!values.lastName.trim()) {
    errors.lastName = 'Nazwisko jest wymagane.';
  }

  if (values.isPublished) {
    if (!values.city.trim()) {
      errors.city =
        'Miejscowość jest wymagana do publikacji.';
    }

    if (!values.practiceName.trim()) {
      errors.practiceName =
        'Nazwa gabinetu jest wymagana do publikacji.';
    }

    if (values.lastName.trim() === '(uzupełnij)') {
      errors.lastName =
        'Uzupełnij nazwisko przed publikacją.';
    }
  }

  if (
    values.postalCode.trim() &&
    !/^[0-9]{2}-[0-9]{3}$/.test(values.postalCode.trim())
  ) {
    errors.postalCode = 'Podaj kod w formacie 00-000.';
  }

  const phone = values.publicPhoneNumber.trim();
  const digits = phone.replace(/[^0-9]/g, '').length;

  if (
    phone &&
    (!/^\+?[0-9 ()-]+$/.test(phone) ||
      digits < 7 ||
      digits > 15)
  ) {
    errors.publicPhoneNumber =
      'Podaj 7–15 cyfr. Dozwolone są + na początku, spacje, nawiasy i myślniki.';
  }

  const email = values.publicEmail.trim();

  if (
    email &&
    !/^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(email)
  ) {
    errors.publicEmail =
      'Podaj pojedynczy adres e-mail gabinetu.';
  }

  if (
    values.specializations.length > 8 ||
    new Set(values.specializations).size !==
      values.specializations.length
  ) {
    errors.specializations =
      'Wybierz maksymalnie 8 różnych specjalizacji.';
  }

  return errors;
}

export function profileServerErrors(error: unknown) {
  const result: Partial<
    Record<keyof ProfileValues, string>
  > = {};

  if (!isAxiosError<ApiProblem>(error)) {
    return result;
  }

  const names: (keyof ProfileValues)[] = [
    ...profileFields.map((field) => field.name),
    'specializations',
    'isPublished',
  ];

  for (const [key, messages] of Object.entries(
    error.response?.data?.errors ?? {},
  )) {
    const last = key.split('.').pop()?.toLowerCase();
    const name = names.find(
      (item) => item.toLowerCase() === last,
    );

    if (name && Array.isArray(messages)) {
      result[name] = messages.join(' ');
    }
  }

  return result;
}

export function photoError(error: unknown) {
  if (isAxiosError<ApiProblem>(error)) {
    if (error.response?.status === 413) {
      return 'Zdjęcie jest za duże. Maksymalny rozmiar to 5 MiB.';
    }

    if (error.response?.status === 409) {
      return 'Zdjęcie zostało zmienione w innej sesji. Odśwież profil i spróbuj ponownie.';
    }

    if (error.response?.status === 503) {
      return 'Przechowywanie zdjęć jest niedostępne. Sprawdź konfigurację Cloudflare R2 lub spróbuj później.';
    }

    const messages = error.response?.data?.errors?.file;

    if (Array.isArray(messages) && messages.length) {
      return messages.join(' ');
    }
  }

  return getApiMessage(error);
}

export const therapistProfileApi = {
  get: (signal?: AbortSignal) =>
    apiClient
      .get<MarketplaceProfile>(
        '/api/therapists/me/profile',
        { signal, timeout: 15_000 },
      )
      .then((response) => response.data),

  specializations: (signal?: AbortSignal) =>
    apiClient
      .get<Specialization[]>(
        '/api/therapists/specializations',
        { signal, timeout: 15_000 },
      )
      .then((response) => response.data),

  save: (values: ProfileValues) => {
    const marketplace = {
      specializations: [...values.specializations],
      ...Object.fromEntries(
        marketplaceFields.map((name) => [
          name,
          values[name].trim() || null,
        ]),
      ),
    };

    return apiClient
      .put<MarketplaceProfile>(
        '/api/therapists/me/profile',
        {
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          city: values.city.trim(),
          practiceName: values.practiceName.trim(),
          bio: values.bio.trim(),
          isPublished: values.isPublished,
          marketplace,
        },
        { timeout: 15_000 },
      )
      .then((response) => response.data);
  },

  photo: (id: string, signal?: AbortSignal) =>
    apiClient
      .get<Blob>(
        `/api/therapists/${encodeURIComponent(id)}/photo`,
        {
          signal,
          responseType: 'blob',
          timeout: 20_000,
        },
      )
      .then((response) => response.data),

  uploadPhoto: (file: File) => {
    const data = new FormData();
    data.append('file', file);

    return apiClient
      .put<MarketplaceProfile>(
        '/api/therapists/me/profile/photo',
        data,
        {
          headers: { 'Content-Type': undefined },
          timeout: 60_000,
        },
      )
      .then((response) => response.data);
  },

  deletePhoto: () =>
    apiClient
      .delete<MarketplaceProfile>(
        '/api/therapists/me/profile/photo',
        { timeout: 30_000 },
      )
      .then((response) => response.data),
};