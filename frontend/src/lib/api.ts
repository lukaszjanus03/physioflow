import { apiClient } from './api-client';
import type {
  CreatePatientPayload,
  PaginatedList,
  PatientDetail,
  PatientListItem,
  TokenResponse,
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

  exportPdf: (id: string) =>
    apiClient
      .get(`/api/patients/${id}/export-pdf`, { responseType: 'blob' })
      .then((r) => r.data as Blob),
};
