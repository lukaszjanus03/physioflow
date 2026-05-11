import type { PainSeverity } from '@/types/api';

export const SEVERITY_COLORS: Record<PainSeverity, string> = {
  AcutePain: '#ef4444',    // czerwony - ból ostry
  ChronicPain: '#f97316',  // pomarańczowy - chroniczny
  Tension: '#eab308',      // żółty - napięcie
};

export const SEVERITY_LABELS: Record<PainSeverity, string> = {
  AcutePain: 'Ból ostry',
  ChronicPain: 'Ból chroniczny',
  Tension: 'Napięcie',
};

export const SEVERITY_DESCRIPTIONS: Record<PainSeverity, string> = {
  AcutePain: 'Silny, nagły ból wymagający uwagi',
  ChronicPain: 'Długotrwały, nawracający ból',
  Tension: 'Napięcie mięśniowe, sztywność',
};

export const ALL_SEVERITIES: PainSeverity[] = ['AcutePain', 'ChronicPain', 'Tension'];

// Proporcje SVG (viewBox) - używane do obliczania rozmiaru Stage
export const BODY_VIEWBOX_WIDTH = 400;
export const BODY_VIEWBOX_HEIGHT = 900;
export const BODY_ASPECT_RATIO = BODY_VIEWBOX_WIDTH / BODY_VIEWBOX_HEIGHT;
