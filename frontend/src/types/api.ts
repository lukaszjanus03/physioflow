export interface PaginatedList<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

export interface PatientListItem {
  id: string;
  firstName: string;
  lastName: string;
  email: string | null;
  phoneNumber: string | null;
  isArchived: boolean;
  createdAt: string;
}

export interface PatientDetail {
  id: string;
  firstName: string;
  lastName: string;
  pesel: string | null;
  dateOfBirth: string | null;
  email: string | null;
  phoneNumber: string | null;
  address: string | null;
  medicalHistory: string | null;
  medications: string | null;
  generalNotes: string | null;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreatePatientPayload {
  firstName: string;
  lastName: string;
  pesel?: string;
  dateOfBirth?: string;
  email?: string;
  phoneNumber?: string;
  address?: string;
  medicalHistory?: string;
  medications?: string;
  generalNotes?: string;
}

export interface UpdatePatientPayload extends CreatePatientPayload {
  id: string;
}

export type AppointmentStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow';

export interface Appointment {
  id: string;
  patientId: string;
  patientName: string;
  startsAt: string;
  endsAt: string;
  durationMinutes: number;
  status: AppointmentStatus;
  reason: string | null;
  notes: string | null;
}

export type BodySide = 'Front' | 'Back';
export type PainSeverity = 'AcutePain' | 'ChronicPain' | 'Tension';

export interface PainPoint {
  id: string;
  side: BodySide;
  x: number;
  y: number;
  severity: PainSeverity;
  painScale?: number | null;
  note: string | null;
}

export interface BodyMap {
  id: string;
  patientId: string;
  appointmentId: string | null;
  recordedAt: string;
  notes: string | null;
  painPoints: PainPoint[];
}

export interface BodyMapSummary {
  id: string;
  patientId: string;
  appointmentId: string | null;
  recordedAt: string;
  notes: string | null;
  createdByRole: 'Therapist' | 'Patient';
  painPointsCount: number;
}

export interface CreateBodyMapPayload {
  patientId?: string;
  appointmentId?: string;
  notes?: string;
  painPoints: Array<{
    side: BodySide;
    x: number;
    y: number;
    severity: PainSeverity;
    painScale?: number;
    note?: string;
  }>;
}

export interface UpdateBodyMapPayload {
  notes?: string;
  painPoints: Array<{
    side: BodySide;
    x: number;
    y: number;
    severity: PainSeverity;
    painScale?: number;
    note?: string;
  }>;
}

export interface ChatMessage {
  senderId: string;
  recipientId?: string;
  content: string;
  sentAt: string;
}

export interface ApiError {
  title: string;
  status: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
