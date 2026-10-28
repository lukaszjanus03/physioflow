import { apiClient } from './api-client';
import type {
  AnatomicalRegionInfo,
  BodyMap,
  BodyMapSummary,
  CreateBodyMapPayload,
  CreatePatientPayload,
  PaginatedList,
  PatientDetail,
  PatientListItem,
  TokenResponse,
  UpdateBodyMapPayload,
  UpdatePatientPayload,
} from '@/types/api';

// --- Auth
export const authApi = {
  login: (email: string, password: string) =>
    apiClient.post<TokenResponse>('/api/auth/login', { email, password }).then((r) => r.data),

  register: (payload: { email: string; password: string; firstName: string; lastName: string }) =>
    apiClient.post<{ userId: string }>('/api/auth/register', payload).then((r) => r.data),

  logout: () => apiClient.post('/api/auth/logout').then((r) => r.data),
};

// --- Patients
export const patientsApi = {
  list: (
    params: {
      pageNumber?: number;
      pageSize?: number;
      search?: string;
      includeArchived?: boolean;
      onlyArchived?: boolean;
    } = {},
  ) =>
    apiClient
      .get<PaginatedList<PatientListItem>>('/api/patients', { params })
      .then((r) => r.data),

  getById: (id: string) =>
    apiClient.get<PatientDetail>(`/api/patients/${id}`).then((r) => r.data),

  create: (payload: CreatePatientPayload) =>
    apiClient.post<{ id: string }>('/api/patients', payload).then((r) => r.data),

  update: (id: string, payload: UpdatePatientPayload) =>
    apiClient.put<void>(`/api/patients/${id}`, payload).then((r) => r.data),

  archive: (id: string) =>
    apiClient.delete<void>(`/api/patients/${id}`).then((r) => r.data),

  unarchive: (id: string) =>
    apiClient.post<void>(`/api/patients/${id}/unarchive`).then((r) => r.data),
};

// --- Body Mapping
export const bodyMapsApi = {
  listByPatient: (patientId: string) =>
    apiClient
      .get<BodyMapSummary[]>(`/api/bodymapping/patient/${patientId}`)
      .then((r) => r.data),

  getById: (id: string) =>
    apiClient.get<BodyMap>(`/api/bodymapping/${id}`).then((r) => r.data),

  create: (payload: CreateBodyMapPayload) =>
    apiClient.post<{ id: string }>('/api/bodymapping', payload).then((r) => r.data),

  update: (id: string, payload: UpdateBodyMapPayload) =>
    apiClient.put<void>(`/api/bodymapping/${id}`, payload).then((r) => r.data),

  delete: (id: string) =>
    apiClient.delete(`/api/bodymapping/${id}`).then((r) => r.data),
};

export const musclesApi = {
  getByRegion: (regionId: string) =>
    apiClient
      .get<AnatomicalRegionInfo>(
        `/api/muscles/by-region/${encodeURIComponent(regionId)}`
      )
      .then((response) => response.data),
};