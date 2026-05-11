'use client';

import dynamic from 'next/dynamic';
import type Konva from 'konva';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Download, MapPinned, RotateCcw, Save, ZoomIn, ZoomOut } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, Label } from '@/components/ui/primitives';
import { Textarea } from '@/components/ui/textarea';
import type { BodySide, PainPoint, PainSeverity } from '@/types/api';
import { PainPointDialog } from './PainPointDialog';
import type { BodyMapCanvasApi, CanvasPainPoint } from './BodyMapCanvas';
import {
  ANATOMICAL_REGIONS,
  BODY_VIEWBOX_HEIGHT,
  BODY_VIEWBOX_WIDTH,
  SEVERITY_COLORS,
  SEVERITY_LABELS,
} from './constants';
import type { AnatomicalRegion } from './constants';

const BodyMapCanvas = dynamic(() => import('./BodyMapCanvas').then((m) => m.BodyMapCanvas), {
  ssr: false,
  loading: () => (
    <div className="flex h-[600px] items-center justify-center text-muted-foreground">
      Ładowanie...
    </div>
  ),
});

interface EditablePoint extends CanvasPainPoint {
  side: BodySide;
}

interface BodyMapEditorProps {
  initialPoints?: PainPoint[];
  initialNotes?: string | null;
  readOnly?: boolean;
  saving?: boolean;
  onSave?: (data: {
    notes: string | null;
    points: Array<{ side: BodySide; x: number; y: number; severity: PainSeverity; note: string | null }>;
  }) => void;
}

export function BodyMapEditor({
  initialPoints = [],
  initialNotes = null,
  readOnly = false,
  saving = false,
  onSave,
}: BodyMapEditorProps) {
  const [activeSide, setActiveSide] = useState<BodySide>('Front');
  const [points, setPoints] = useState<EditablePoint[]>(() =>
    initialPoints.map((p) => ({
      id: p.id,
      side: p.side,
      x: p.x,
      y: p.y,
      severity: p.severity,
      note: p.note,
    })),
  );
  const [notes, setNotes] = useState(initialNotes ?? '');
  const [showRegions, setShowRegions] = useState(true);

  const [modalOpen, setModalOpen] = useState(false);
  const [modalMode, setModalMode] = useState<'create' | 'edit'>('create');
  const [pendingCoords, setPendingCoords] = useState<{ x: number; y: number } | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);

  const frontStageRef = useRef<Konva.Stage>(null);
  const backStageRef = useRef<Konva.Stage>(null);
  const viewportApiRef = useRef<BodyMapCanvasApi | null>(null);

  const bindViewportApi = useCallback((api: BodyMapCanvasApi) => {
    viewportApiRef.current = api;
  }, []);

  const containerRef = useRef<HTMLDivElement>(null);
  const [canvasSize, setCanvasSize] = useState({ width: 400, height: 900 });

  useEffect(() => {
    const update = () => {
      if (!containerRef.current) return;

      const w = Math.min(containerRef.current.clientWidth, 450);
      const h = (w * BODY_VIEWBOX_HEIGHT) / BODY_VIEWBOX_WIDTH;

      setCanvasSize({ width: w, height: h });
    };

    update();
    window.addEventListener('resize', update);

    return () => window.removeEventListener('resize', update);
  }, []);

  const pointsForSide = points.filter((p) => p.side === activeSide);
  const regionsForSide = ANATOMICAL_REGIONS.filter((r) => r.side === activeSide);
  const editingPoint = editingId ? points.find((p) => p.id === editingId) : null;

  const handleStageClick = useCallback((x: number, y: number) => {
    setModalMode('create');
    setEditingId(null);
    setPendingCoords({ x, y });
    setModalOpen(true);
  }, []);

  const handleRegionClick = useCallback((_region: AnatomicalRegion, point: { x: number; y: number }) => {
    setModalMode('create');
    setEditingId(null);
    setPendingCoords(point);
    setModalOpen(true);
  }, []);

  const handlePointClick = useCallback((id: string) => {
    setModalMode('edit');
    setEditingId(id);
    setPendingCoords(null);
    setModalOpen(true);
  }, []);

  const handleSavePoint = (severity: PainSeverity, note: string | null) => {
    if (modalMode === 'create' && pendingCoords) {
      setPoints((ps) => [
        ...ps,
        {
          id: crypto.randomUUID(),
          side: activeSide,
          x: pendingCoords.x,
          y: pendingCoords.y,
          severity,
          note,
        },
      ]);
    } else if (modalMode === 'edit' && editingId) {
      setPoints((ps) => ps.map((p) => (p.id === editingId ? { ...p, severity, note } : p)));
    }

    setModalOpen(false);
  };

  const handleDeletePoint = () => {
    if (!editingId) return;

    setPoints((ps) => ps.filter((p) => p.id !== editingId));
    setModalOpen(false);
  };

  const handleExportPng = () => {
    const stage = activeSide === 'Front' ? frontStageRef.current : backStageRef.current;
    if (!stage) return;

    const dataUrl = stage.toDataURL({ pixelRatio: 2 });
    const link = document.createElement('a');

    link.download = `body-map-${activeSide.toLowerCase()}-${Date.now()}.png`;
    link.href = dataUrl;
    link.click();
  };

  const handleSubmit = () => {
    if (!onSave) return;

    onSave({
      notes: notes.trim() || null,
      points: points.map((p) => ({
        side: p.side,
        x: p.x,
        y: p.y,
        severity: p.severity,
        note: p.note,
      })),
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex gap-2 border-b">
        <button
          type="button"
          onClick={() => setActiveSide('Front')}
          className={`px-4 py-2 text-sm font-medium transition-colors ${
            activeSide === 'Front'
              ? 'border-b-2 border-primary text-primary'
              : 'text-muted-foreground hover:text-foreground'
          }`}
        >
          Przód ({points.filter((p) => p.side === 'Front').length})
        </button>
        <button
          type="button"
          onClick={() => setActiveSide('Back')}
          className={`px-4 py-2 text-sm font-medium transition-colors ${
            activeSide === 'Back'
              ? 'border-b-2 border-primary text-primary'
              : 'text-muted-foreground hover:text-foreground'
          }`}
        >
          Tył ({points.filter((p) => p.side === 'Back').length})
        </button>
      </div>

      <div className="grid gap-6 lg:grid-cols-[auto_1fr]">
        <Card className="p-4">
          <div ref={containerRef} className="relative mx-auto" style={{ width: canvasSize.width }}>
            <div className="mb-2 flex flex-wrap items-center justify-center gap-2 sm:justify-start">
              <span className="text-xs text-muted-foreground">Powiększenie:</span>

              <Button
                type="button"
                variant="outline"
                size="icon"
                className="h-8 w-8 shrink-0"
                aria-label="Oddal"
                onClick={() => viewportApiRef.current?.zoomOut()}
              >
                <ZoomOut className="h-4 w-4" />
              </Button>

              <Button
                type="button"
                variant="outline"
                size="icon"
                className="h-8 w-8 shrink-0"
                aria-label="Przybliż"
                onClick={() => viewportApiRef.current?.zoomIn()}
              >
                <ZoomIn className="h-4 w-4" />
              </Button>

              <Button
                type="button"
                variant="outline"
                size="icon"
                className="h-8 w-8 shrink-0"
                aria-label="Widok domyślny"
                onClick={() => viewportApiRef.current?.resetView()}
              >
                <RotateCcw className="h-4 w-4" />
              </Button>

              <Button
                type="button"
                variant={showRegions ? 'default' : 'outline'}
                size="icon"
                className="h-8 w-8 shrink-0"
                aria-label="Regiony anatomiczne"
                onClick={() => setShowRegions((v) => !v)}
              >
                <MapPinned className="h-4 w-4" />
              </Button>
            </div>

            <div
              className="relative overflow-hidden rounded-sm bg-white"
              style={{ width: canvasSize.width, height: canvasSize.height }}
            >
              {activeSide === 'Front' ? (
                <BodyMapCanvas
                  stageRef={frontStageRef}
                  silhouetteUrl="/body-silhouettes/body-front.png"
                  width={canvasSize.width}
                  height={canvasSize.height}
                  points={pointsForSide}
                  readOnly={readOnly}
                  showAnatomicalRegions={showRegions}
                  anatomicalRegions={regionsForSide}
                  onViewportApi={bindViewportApi}
                  onStageClick={handleStageClick}
                  onPointClick={handlePointClick}
                  onRegionClick={handleRegionClick}
                />
              ) : (
                <BodyMapCanvas
                  stageRef={backStageRef}
                  silhouetteUrl="/body-silhouettes/body-back.png"
                  width={canvasSize.width}
                  height={canvasSize.height}
                  points={pointsForSide}
                  readOnly={readOnly}
                  showAnatomicalRegions={showRegions}
                  anatomicalRegions={regionsForSide}
                  onViewportApi={bindViewportApi}
                  onStageClick={handleStageClick}
                  onPointClick={handlePointClick}
                  onRegionClick={handleRegionClick}
                />
              )}
            </div>
          </div>

          <div className="mt-4 flex flex-wrap justify-center gap-3 text-xs">
            {(Object.keys(SEVERITY_COLORS) as PainSeverity[]).map((s) => (
              <div key={s} className="flex items-center gap-1.5">
                <div className="h-3 w-3 rounded-full" style={{ backgroundColor: SEVERITY_COLORS[s] }} />
                <span>{SEVERITY_LABELS[s]}</span>
              </div>
            ))}
          </div>
        </Card>

        <div className="space-y-4">
          <Card className="p-4">
            <Label htmlFor="notes" className="mb-2 block">
              Notatka ogólna
            </Label>
            <Textarea
              id="notes"
              placeholder="Ogólne obserwacje dotyczące mapy bólu..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              disabled={readOnly}
              rows={5}
              maxLength={2000}
            />
          </Card>

          {points.length > 0 && (
            <Card className="p-4">
              <Label className="mb-2 block">Zaznaczone punkty ({points.length})</Label>
              <ul className="max-h-60 space-y-1 overflow-y-auto text-sm">
                {points.map((p, i) => (
                  <li key={p.id} className="flex items-start gap-2 rounded p-1.5 hover:bg-accent">
                    <div
                      className="mt-1 h-3 w-3 flex-shrink-0 rounded-full"
                      style={{ backgroundColor: SEVERITY_COLORS[p.severity] }}
                    />
                    <div className="flex-1">
                      <div className="text-xs text-muted-foreground">
                        #{i + 1} · {p.side === 'Front' ? 'Przód' : 'Tył'} · {SEVERITY_LABELS[p.severity]}
                      </div>
                      {p.note && <div className="text-xs">{p.note}</div>}
                    </div>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={handleExportPng} type="button">
              <Download className="mr-2 h-4 w-4" />
              Eksportuj PNG
            </Button>

            {!readOnly && onSave && (
              <Button onClick={handleSubmit} disabled={saving}>
                <Save className="mr-2 h-4 w-4" />
                {saving ? 'Zapisywanie...' : 'Zapisz mapę'}
              </Button>
            )}
          </div>
        </div>
      </div>

      <PainPointDialog
        open={modalOpen}
        onOpenChange={setModalOpen}
        initialSeverity={editingPoint?.severity}
        initialNote={editingPoint?.note}
        onSave={handleSavePoint}
        onDelete={modalMode === 'edit' ? handleDeletePoint : undefined}
      />
    </div>
  );
}
