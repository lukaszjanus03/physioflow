'use client';

import dynamic from 'next/dynamic';
import type Konva from 'konva';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Activity,
  Download,
  Eye,
  Flame,
  Gauge,
  ListChecks,
  MapPinned,
  Pencil,
  RotateCcw,
  Save,
  SplitSquareHorizontal,
  Trash2,
  ZoomIn,
  ZoomOut,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card, Label } from '@/components/ui/primitives';
import { Textarea } from '@/components/ui/textarea';
import type { BodySide, PainPoint, PainSeverity } from '@/types/api';
import { AnatomicalRegionInfoDialog } from './AnatomicalRegionInfoDialog';
import { PainPointDialog } from './PainPointDialog';
import type { BodyMapCanvasApi, CanvasPainPoint } from './BodyMapCanvas';
import {
  ANATOMICAL_REGIONS,
  BODY_IMAGE_BOUNDS,
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
  painScale: number;
  regionId?: string | null;
}

interface BodyMapEditorProps {
  initialPoints?: PainPoint[];
  initialNotes?: string | null;
  readOnly?: boolean;
  saving?: boolean;
  onSave?: (data: {
    notes: string | null;
    points: Array<{
      side: BodySide;
      x: number;
      y: number;
      severity: PainSeverity;
      painScale: number;
      note: string | null;
    }>;
  }) => void;
}

type PointListFilter = 'current' | 'all';

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
      painScale: getPainScaleValue(p.painScale),
      regionId: null,
    })),
  );
  const [notes, setNotes] = useState(initialNotes ?? '');
  const [showRegions, setShowRegions] = useState(true);
  const [pointListFilter, setPointListFilter] = useState<PointListFilter>('current');

  const [painPointDialogOpen, setPainPointDialogOpen] = useState(false);
  const [regionInfoDialogOpen, setRegionInfoDialogOpen] = useState(false);
  const [modalMode, setModalMode] = useState<'create' | 'edit'>('create');
  const [pendingCoords, setPendingCoords] = useState<{ x: number; y: number } | null>(null);
  const [selectedRegion, setSelectedRegion] = useState<AnatomicalRegion | null>(null);
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

  const frontPointsCount = points.filter((p) => p.side === 'Front').length;
  const backPointsCount = points.filter((p) => p.side === 'Back').length;

  const visiblePoints = useMemo(() => {
    if (pointListFilter === 'all') return points;
    return points.filter((point) => point.side === activeSide);
  }, [activeSide, pointListFilter, points]);

  const summary = useMemo(() => getPainMapSummary(points), [points]);

  const openPainPointCreateDialog = useCallback(
    (point: { x: number; y: number }) => {
      if (readOnly) return;

      setModalMode('create');
      setEditingId(null);
      setPendingCoords(point);
      setPainPointDialogOpen(true);
    },
    [readOnly],
  );

  const openPainPointEditDialog = useCallback(
    (id: string) => {
      if (readOnly) return;

      const point = points.find((p) => p.id === id);
      if (point) setActiveSide(point.side);

      setModalMode('edit');
      setEditingId(id);
      setPendingCoords(null);
      setSelectedRegion(null);
      setPainPointDialogOpen(true);
    },
    [points, readOnly],
  );

  const handleStageClick = useCallback(
    (x: number, y: number) => {
      setSelectedRegion(null);
      openPainPointCreateDialog({ x, y });
    },
    [openPainPointCreateDialog],
  );

  const handleRegionClick = useCallback((region: AnatomicalRegion, point: { x: number; y: number }) => {
    setSelectedRegion(region);
    setPendingCoords(point);
    setRegionInfoDialogOpen(true);
  }, []);

  const handleAddPainPointFromRegion = () => {
    if (!pendingCoords) return;

    setRegionInfoDialogOpen(false);
    openPainPointCreateDialog(pendingCoords);
  };

  const handlePointClick = useCallback(
    (id: string) => {
      openPainPointEditDialog(id);
    },
    [openPainPointEditDialog],
  );

  const handleSavePoint = (severity: PainSeverity, note: string | null, painScale: number) => {
    if (readOnly) return;

    if (modalMode === 'create' && pendingCoords) {
      setPoints((ps) => [
        ...ps,
        {
          id: crypto.randomUUID(),
          side: activeSide,
          x: pendingCoords.x,
          y: pendingCoords.y,
          severity,
          painScale,
          note,
          regionId: selectedRegion?.id ?? null,
        },
      ]);
    } else if (modalMode === 'edit' && editingId) {
      setPoints((ps) =>
        ps.map((p) => (p.id === editingId ? { ...p, severity, painScale, note } : p)),
      );
    }

    setPainPointDialogOpen(false);
    setPendingCoords(null);
    setSelectedRegion(null);
  };

  const handleDeletePoint = () => {
    if (!editingId || readOnly) return;

    setPoints((ps) => ps.filter((p) => p.id !== editingId));
    setPainPointDialogOpen(false);
    setEditingId(null);
  };

  const handleDeletePointFromList = (point: EditablePoint) => {
    if (readOnly) return;

    const confirmed = window.confirm(
      `Czy na pewno chcesz usunąć punkt: ${SEVERITY_LABELS[point.severity]}?`,
    );

    if (!confirmed) return;

    setPoints((ps) => ps.filter((p) => p.id !== point.id));

    if (editingId === point.id) {
      setEditingId(null);
      setPainPointDialogOpen(false);
    }
  };

  const handleShowPoint = (point: EditablePoint) => {
    setActiveSide(point.side);
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
        painScale: p.painScale,
        note: p.note,
      })),
    });
  };

  return (
    <div className="space-y-4">
      <PainMapSummary summary={summary} />

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
          Przód ({frontPointsCount})
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
          Tył ({backPointsCount})
        </button>
      </div>

      <div className="grid gap-6 xl:grid-cols-[auto_420px]">
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
            {(Object.keys(SEVERITY_COLORS) as PainSeverity[]).map((severity) => (
              <div key={severity} className="flex items-center gap-1.5">
                <div
                  className="h-3 w-3 rounded-full"
                  style={{ backgroundColor: SEVERITY_COLORS[severity] }}
                />
                <span>{SEVERITY_LABELS[severity]}</span>
              </div>
            ))}
          </div>
        </Card>

        <div className="space-y-4">
          <PainPointsPanel
            points={visiblePoints}
            allPointsCount={points.length}
            activeSide={activeSide}
            filter={pointListFilter}
            readOnly={readOnly}
            onFilterChange={setPointListFilter}
            onShowPoint={handleShowPoint}
            onEditPoint={openPainPointEditDialog}
            onDeletePoint={handleDeletePointFromList}
          />

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

      <AnatomicalRegionInfoDialog
        open={regionInfoDialogOpen}
        region={selectedRegion}
        onOpenChange={setRegionInfoDialogOpen}
        onAddPainPoint={handleAddPainPointFromRegion}
      />

      <PainPointDialog
        open={painPointDialogOpen}
        onOpenChange={setPainPointDialogOpen}
        initialSeverity={editingPoint?.severity}
        initialPainScale={editingPoint?.painScale}
        initialNote={editingPoint?.note}
        onSave={handleSavePoint}
        onDelete={!readOnly && modalMode === 'edit' ? handleDeletePoint : undefined}
      />
    </div>
  );
}

function PainMapSummary({ summary }: { summary: PainMapSummaryData }) {
  return (
    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      <SummaryTile
        icon={<Activity className="h-5 w-5" />}
        label="Wszystkie punkty"
        value={String(summary.total)}
        description={summary.total === 0 ? 'Brak zaznaczeń' : getPointCountLabel(summary.total)}
      />

      <SummaryTile
        icon={<Flame className="h-5 w-5" />}
        label="Ból ostry"
        value={String(summary.acute)}
        description={summary.acute === 0 ? 'Brak ostrych punktów' : 'Wymaga uwagi'}
        accent="danger"
      />

      <SummaryTile
        icon={<SplitSquareHorizontal className="h-5 w-5" />}
        label="Przód / tył"
        value={`${summary.front} / ${summary.back}`}
        description="Rozkład punktów"
      />

      <SummaryTile
        icon={<Gauge className="h-5 w-5" />}
        label="Średnia skala"
        value={summary.averagePainScale !== null ? `${summary.averagePainScale}/10` : 'Brak'}
        description={summary.dominantSeverity ? `Dominujący typ: ${SEVERITY_LABELS[summary.dominantSeverity]}` : 'Dodaj pierwszy punkt'}
        severity={summary.dominantSeverity}
      />
    </div>
  );
}

function SummaryTile({
  icon,
  label,
  value,
  description,
  accent = 'default',
  severity,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  description: string;
  accent?: 'default' | 'danger';
  severity?: PainSeverity | null;
}) {
  const color = severity ? SEVERITY_COLORS[severity] : undefined;

  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm text-muted-foreground">{label}</p>
          <p className="mt-1 text-2xl font-semibold">{value}</p>
          <p className="mt-1 text-xs text-muted-foreground">{description}</p>
        </div>

        <div
          className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${
            accent === 'danger' ? 'bg-red-50 text-red-600' : 'bg-primary/10 text-primary'
          }`}
          style={color ? { backgroundColor: `${color}18`, color } : undefined}
        >
          {icon}
        </div>
      </div>
    </Card>
  );
}

function PainPointsPanel({
  points,
  allPointsCount,
  activeSide,
  filter,
  readOnly,
  onFilterChange,
  onShowPoint,
  onEditPoint,
  onDeletePoint,
}: {
  points: EditablePoint[];
  allPointsCount: number;
  activeSide: BodySide;
  filter: PointListFilter;
  readOnly: boolean;
  onFilterChange: (filter: PointListFilter) => void;
  onShowPoint: (point: EditablePoint) => void;
  onEditPoint: (id: string) => void;
  onDeletePoint: (point: EditablePoint) => void;
}) {
  const currentSideLabel = getSideLabel(activeSide);

  return (
    <Card className="overflow-hidden">
      <div className="border-b p-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <div className="flex items-center gap-2">
              <ListChecks className="h-5 w-5 text-primary" />
              <Label className="text-base">Punkty bólu</Label>
            </div>

            <p className="mt-1 text-sm text-muted-foreground">
              {allPointsCount > 0
                ? `Łącznie zapisano ${allPointsCount} ${getPointCountLabel(allPointsCount)}.`
                : 'Dodane punkty pojawią się w tej liście.'}
            </p>
          </div>

          <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary">
            {allPointsCount}
          </span>
        </div>

        <div className="mt-4 grid grid-cols-2 rounded-lg bg-muted p-1">
          <button
            type="button"
            onClick={() => onFilterChange('current')}
            className={`rounded-md px-3 py-2 text-sm font-medium transition ${
              filter === 'current'
                ? 'bg-background text-foreground shadow-sm'
                : 'text-muted-foreground hover:text-foreground'
            }`}
          >
            {currentSideLabel}
          </button>

          <button
            type="button"
            onClick={() => onFilterChange('all')}
            className={`rounded-md px-3 py-2 text-sm font-medium transition ${
              filter === 'all'
                ? 'bg-background text-foreground shadow-sm'
                : 'text-muted-foreground hover:text-foreground'
            }`}
          >
            Wszystkie
          </button>
        </div>
      </div>

      {points.length === 0 ? (
        <div className="p-6 text-center">
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-muted">
            <MapPinned className="h-6 w-6 text-muted-foreground" />
          </div>

          <p className="mt-3 text-sm font-medium">
            {filter === 'current' ? `Brak punktów: ${currentSideLabel.toLowerCase()}` : 'Brak punktów bólu'}
          </p>

          <p className="mt-1 text-xs text-muted-foreground">
            Kliknij na sylwetkę lub region anatomiczny, żeby dodać pierwszy punkt.
          </p>
        </div>
      ) : (
        <div className="max-h-[520px] divide-y overflow-y-auto">
          {points.map((point, index) => (
            <PainPointListItem
              key={point.id}
              point={point}
              index={index}
              activeSide={activeSide}
              filter={filter}
              readOnly={readOnly}
              onShowPoint={onShowPoint}
              onEditPoint={onEditPoint}
              onDeletePoint={onDeletePoint}
            />
          ))}
        </div>
      )}
    </Card>
  );
}

function PainPointListItem({
  point,
  index,
  activeSide,
  filter,
  readOnly,
  onShowPoint,
  onEditPoint,
  onDeletePoint,
}: {
  point: EditablePoint;
  index: number;
  activeSide: BodySide;
  filter: PointListFilter;
  readOnly: boolean;
  onShowPoint: (point: EditablePoint) => void;
  onEditPoint: (id: string) => void;
  onDeletePoint: (point: EditablePoint) => void;
}) {
  const regionName = getPointRegionName(point);
  const shouldShowSwitchSideButton = filter === 'all' && point.side !== activeSide;

  return (
    <div className="p-4 transition hover:bg-muted/40">
      <div className="flex items-start gap-3">
        <div
          className="mt-1 h-3 w-3 shrink-0 rounded-full ring-4 ring-background"
          style={{ backgroundColor: SEVERITY_COLORS[point.severity] }}
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <p className="font-medium">
              #{index + 1} {regionName}
            </p>

            <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
              {getSideLabel(point.side)}
            </span>
          </div>

          <p className="mt-1 text-sm text-muted-foreground">
            {SEVERITY_LABELS[point.severity]} · skala {point.painScale}/10
          </p>

          {point.note ? (
            <p className="mt-2 rounded-md bg-muted/60 p-2 text-sm text-muted-foreground">
              {point.note}
            </p>
          ) : (
            <p className="mt-2 text-xs italic text-muted-foreground">Brak notatki</p>
          )}

          <div className="mt-3 flex flex-wrap gap-2">
            {shouldShowSwitchSideButton && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => onShowPoint(point)}
              >
                <Eye className="mr-2 h-4 w-4" />
                Pokaż na {getSideLabel(point.side).toLowerCase()}
              </Button>
            )}

            {!readOnly && (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => onEditPoint(point.id)}
                >
                  <Pencil className="mr-2 h-4 w-4" />
                  Edytuj
                </Button>

                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="text-destructive hover:text-destructive"
                  onClick={() => onDeletePoint(point)}
                >
                  <Trash2 className="mr-2 h-4 w-4" />
                  Usuń
                </Button>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

interface PainMapSummaryData {
  total: number;
  acute: number;
  chronic: number;
  tension: number;
  front: number;
  back: number;
  dominantSeverity: PainSeverity | null;
  averagePainScale: number | null;
}

function getPainMapSummary(points: EditablePoint[]): PainMapSummaryData {
  const severityCounts: Record<PainSeverity, number> = {
    AcutePain: 0,
    ChronicPain: 0,
    Tension: 0,
  };

  let front = 0;
  let back = 0;

  points.forEach((point) => {
    severityCounts[point.severity] += 1;

    if (point.side === 'Front') {
      front += 1;
    } else {
      back += 1;
    }
  });

  const dominantSeverity = (Object.keys(severityCounts) as PainSeverity[]).reduce<PainSeverity | null>(
    (dominant, severity) => {
      if (severityCounts[severity] === 0) return dominant;
      if (!dominant) return severity;
      return severityCounts[severity] > severityCounts[dominant] ? severity : dominant;
    },
    null,
  );

  const averagePainScale =
    points.length > 0
      ? Number((points.reduce((sum, point) => sum + point.painScale, 0) / points.length).toFixed(1))
      : null;

  return {
    total: points.length,
    acute: severityCounts.AcutePain,
    chronic: severityCounts.ChronicPain,
    tension: severityCounts.Tension,
    front,
    back,
    dominantSeverity,
    averagePainScale,
  };
}

function getSideLabel(side: BodySide) {
  return side === 'Front' ? 'Przód' : 'Tył';
}

function getPointCountLabel(count: number) {
  if (count === 1) return 'punkt';
  if (count >= 2 && count <= 4) return 'punkty';
  return 'punktów';
}

function getPointRegionName(point: EditablePoint) {
  if (point.regionId) {
    const region = ANATOMICAL_REGIONS.find((item) => item.id === point.regionId);
    if (region) return region.name;
  }

  const matchedRegion = findRegionForPoint(point);
  return matchedRegion?.name ?? 'Własny punkt';
}

function findRegionForPoint(point: EditablePoint) {
  const bounds = BODY_IMAGE_BOUNDS[point.side];

  return ANATOMICAL_REGIONS.find((region) => {
    if (region.side !== point.side) return false;

    const x = bounds.x + region.x * bounds.width;
    const y = bounds.y + region.y * bounds.height;
    const width = region.width * bounds.width;
    const height = region.height * bounds.height;

    return point.x >= x && point.x <= x + width && point.y >= y && point.y <= y + height;
  });
}

function getPainScaleValue(value: number | null | undefined) {
  if (!value) return 5;
  return Math.min(10, Math.max(1, value));
}
