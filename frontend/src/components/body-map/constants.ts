import type { BodySide, PainSeverity } from '@/types/api';

export const SEVERITY_COLORS: Record<PainSeverity, string> = {
  AcutePain: '#ef4444',
  ChronicPain: '#f97316',
  Tension: '#eab308',
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

export const BODY_VIEWBOX_WIDTH = 400;
export const BODY_VIEWBOX_HEIGHT = 900;
export const BODY_ASPECT_RATIO = BODY_VIEWBOX_WIDTH / BODY_VIEWBOX_HEIGHT;

export const BODY_IMAGE_BOUNDS: Record<BodySide, { x: number; y: number; width: number; height: number }> = {
  Front: { x: 0.0833, y: 0.0088, width: 0.8194, height: 0.9736 },
  Back: { x: 0.0816, y: 0.0107, width: 0.8351, height: 0.9658 },
};

export interface AnatomicalRegion {
  id: string;
  side: BodySide;
  name: string;
  shape: 'ellipse' | 'rect';
  x: number;
  y: number;
  width: number;
  height: number;
  rotation?: number;
}

export const ANATOMICAL_REGIONS: AnatomicalRegion[] = [
  { id: 'front-neck', side: 'Front', name: 'Szyja', shape: 'rect', x: 0.445, y: 0.135, width: 0.11, height: 0.045 },

  { id: 'front-right-shoulder', side: 'Front', name: 'Prawy bark', shape: 'rect', x: 0.205, y: 0.170, width: 0.15, height: 0.045 },
  { id: 'front-left-shoulder', side: 'Front', name: 'Lewy bark', shape: 'rect', x: 0.645, y: 0.170, width: 0.15, height: 0.045 },

  { id: 'front-right-pec', side: 'Front', name: 'Prawy mięsień piersiowy', shape: 'rect', x: 0.355, y: 0.205, width: 0.12, height: 0.07 },
  { id: 'front-left-pec', side: 'Front', name: 'Lewy mięsień piersiowy', shape: 'rect', x: 0.525, y: 0.205, width: 0.12, height: 0.07 },
  { id: 'front-sternum', side: 'Front', name: 'Mostek', shape: 'rect', x: 0.48, y: 0.225, width: 0.04, height: 0.12 },

  { id: 'front-upper-abdomen', side: 'Front', name: 'Brzuch górny', shape: 'rect', x: 0.425, y: 0.325, width: 0.15, height: 0.105 },
  { id: 'front-lower-abdomen', side: 'Front', name: 'Brzuch dolny', shape: 'rect', x: 0.425, y: 0.415, width: 0.15, height: 0.065 },

  { id: 'front-right-biceps', side: 'Front', name: 'Prawy biceps', shape: 'rect', x: 0.24, y: 0.225, width: 0.065, height: 0.11, rotation: 13 },
  { id: 'front-left-biceps', side: 'Front', name: 'Lewy biceps', shape: 'rect', x: 0.695, y: 0.225, width: 0.065, height: 0.11, rotation: -13 },
  { id: 'front-right-forearm', side: 'Front', name: 'Prawe przedramię', shape: 'rect', x: 0.170, y: 0.365, width: 0.06, height: 0.105, rotation: 13 },
  { id: 'front-left-forearm', side: 'Front', name: 'Lewe przedramię', shape: 'rect', x: 0.770, y: 0.365, width: 0.06, height: 0.105, rotation: -13 },
  { id: 'front-right-hand', side: 'Front', name: 'Prawa dłoń', shape: 'rect', x: 0.055, y: 0.485, width: 0.10, height: 0.07 },
  { id: 'front-left-hand', side: 'Front', name: 'Lewa dłoń', shape: 'rect', x: 0.845, y: 0.485, width: 0.10, height: 0.07 },

  { id: 'front-right-thigh', side: 'Front', name: 'Prawe udo przednie', shape: 'rect', x: 0.30, y: 0.485, width: 0.195, height: 0.13 },
  { id: 'front-left-thigh', side: 'Front', name: 'Lewe udo przednie', shape: 'rect', x: 0.505, y: 0.485, width: 0.195, height: 0.13 },
  { id: 'front-right-knee', side: 'Front', name: 'Prawe kolano', shape: 'rect', x: 0.35, y: 0.675, width: 0.100, height: 0.045 },
  { id: 'front-left-knee', side: 'Front', name: 'Lewe kolano', shape: 'rect', x: 0.545, y: 0.675, width: 0.100, height: 0.045 },
  { id: 'front-right-shin', side: 'Front', name: 'Prawa goleń', shape: 'rect', x: 0.375, y: 0.750, width: 0.08, height: 0.15 },
  { id: 'front-left-shin', side: 'Front', name: 'Lewa goleń', shape: 'rect', x: 0.535, y: 0.750, width: 0.08, height: 0.15 },
  { id: 'front-right-foot', side: 'Front', name: 'Prawa stopa', shape: 'rect', x: 0.355, y: 0.915, width: 0.10, height: 0.075, },
  { id: 'front-left-foot', side: 'Front', name: 'Lewa stopa', shape: 'rect', x: 0.535, y: 0.915, width: 0.10, height: 0.075 },

  { id: 'back-neck', side: 'Back', name: 'Kark', shape: 'rect', x: 0.445, y: 0.100, width: 0.11, height: 0.075 },

  { id: 'back-left-trapezius', side: 'Back', name: 'Lewy czworoboczny', shape: 'rect', x: 0.335, y: 0.170, width: 0.13, height: 0.070 },
  { id: 'back-right-trapezius', side: 'Back', name: 'Prawy czworoboczny', shape: 'rect', x: 0.535, y: 0.170, width: 0.13, height: 0.070 },

  { id: 'back-left-rear-shoulder', side: 'Back', name: 'Lewy bark tylny', shape: 'rect', x: 0.205, y: 0.185, width: 0.15, height: 0.050 },
  { id: 'back-right-rear-shoulder', side: 'Back', name: 'Prawy bark tylny', shape: 'rect', x: 0.645, y: 0.185, width: 0.15, height: 0.050 },

  { id: 'back-left-scapula', side: 'Back', name: 'Lewa łopatka', shape: 'rect', x: 0.345, y: 0.205, width: 0.10, height: 0.085, rotation: 10 },
  { id: 'back-right-scapula', side: 'Back', name: 'Prawa łopatka', shape: 'rect', x: 0.525, y: 0.215, width: 0.10, height: 0.085, rotation: -10 },

  { id: 'back-thoracic-spine', side: 'Back', name: 'Odcinek piersiowy', shape: 'rect', x: 0.480, y: 0.220, width: 0.04, height: 0.175 },
  { id: 'back-lumbar-spine', side: 'Back', name: 'Odcinek lędźwiowy', shape: 'rect', x: 0.375, y: 0.390, width: 0.25, height: 0.045 },

  { id: 'back-left-triceps', side: 'Back', name: 'Lewy triceps', shape: 'rect', x: 0.695, y: 0.235, width: 0.065, height: 0.11, rotation: -13 },
  { id: 'back-right-triceps', side: 'Back', name: 'Prawy triceps', shape: 'rect', x: 0.240, y: 0.235, width: 0.065, height: 0.11, rotation: 13 },

  { id: 'back-left-forearm', side: 'Back', name: 'Lewe przedramię tylne', shape: 'rect', x: 0.770, y: 0.370, width: 0.06, height: 0.105, rotation: -13 },
  { id: 'back-right-forearm', side: 'Back', name: 'Prawe przedramię tylne', shape: 'rect', x: 0.170, y: 0.370, width: 0.06, height: 0.105, rotation: 13 },

  { id: 'back-left-hand', side: 'Back', name: 'Lewa dłoń', shape: 'rect', x: 0.845, y: 0.505, width: 0.10, height: 0.07 },
  { id: 'back-right-hand', side: 'Back', name: 'Prawa dłoń', shape: 'rect', x: 0.055, y: 0.505, width: 0.10, height: 0.07 },

  { id: 'back-left-glute', side: 'Back', name: 'Lewy pośladek', shape: 'rect', x: 0.330, y: 0.435, width: 0.16, height: 0.11 },
  { id: 'back-right-glute', side: 'Back', name: 'Prawy pośladek', shape: 'rect', x: 0.510, y: 0.435, width: 0.16, height: 0.11 },

  { id: 'back-left-hamstring', side: 'Back', name: 'Lewe udo tylne', shape: 'rect', x: 0.300, y: 0.530, width: 0.195, height: 0.13 },
  { id: 'back-right-hamstring', side: 'Back', name: 'Prawe udo tylne', shape: 'rect', x: 0.505, y: 0.530, width: 0.195, height: 0.13 },

  { id: 'back-left-knee', side: 'Back', name: 'Lewe kolano tylne', shape: 'rect', x: 0.350, y: 0.715, width: 0.100, height: 0.045 },
  { id: 'back-right-knee', side: 'Back', name: 'Prawe kolano tylne', shape: 'rect', x: 0.545, y: 0.715, width: 0.100, height: 0.045 },

  { id: 'back-left-calf', side: 'Back', name: 'Lewa łydka', shape: 'rect', x: 0.365, y: 0.740, width: 0.08, height: 0.15 },
  { id: 'back-right-calf', side: 'Back', name: 'Prawa łydka', shape: 'rect', x: 0.555, y: 0.740, width: 0.08, height: 0.15 },

  { id: 'back-left-foot', side: 'Back', name: 'Lewa stopa', shape: 'rect', x: 0.355, y: 0.930, width: 0.10, height: 0.065 },
  { id: 'back-right-foot', side: 'Back', name: 'Prawa stopa', shape: 'rect', x: 0.535, y: 0.930, width: 0.10, height: 0.065 },
];

