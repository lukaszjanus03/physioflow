'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import type Konva from 'konva';
import type { KonvaEventObject } from 'konva/lib/Node';
import {
  Circle,
  Ellipse,
  Group,
  Image as KonvaImage,
  Layer,
  Rect,
  Stage,
  Text,
} from 'react-konva';
import { BODY_IMAGE_BOUNDS, SEVERITY_COLORS } from './constants';
import type { AnatomicalRegion } from './constants';
import type { PainSeverity } from '@/types/api';

export interface CanvasPainPoint {
  id: string;
  x: number;
  y: number;
  severity: PainSeverity;
  note: string | null;
}

export interface BodyMapCanvasApi {
  zoomIn: () => void;
  zoomOut: () => void;
  resetView: () => void;
}

interface BodyMapCanvasProps {
  width: number;
  height: number;
  silhouetteUrl: string;
  points: CanvasPainPoint[];
  readOnly?: boolean;
  showAnatomicalRegions?: boolean;
  anatomicalRegions?: AnatomicalRegion[];
  onStageClick: (x: number, y: number) => void;
  onPointClick: (id: string) => void;
  onRegionClick?: (region: AnatomicalRegion, point: { x: number; y: number }) => void;
  stageRef?: React.RefObject<Konva.Stage>;
  onViewportApi?: (api: BodyMapCanvasApi) => void;
}

const SCALE_MIN = 1;
const SCALE_MAX = 4;
const SCALE_STEP_BUTTON = 0.25;
const SCALE_STEP_WHEEL = 0.08;

function clampScale(s: number) {
  return Math.min(SCALE_MAX, Math.max(SCALE_MIN, s));
}

function clampPosition(scale: number, x: number, y: number, width: number, height: number) {
  if (scale <= 1) return { x: 0, y: 0 };

  const scaledW = width * scale;
  const scaledH = height * scale;
  const minX = Math.min(0, width - scaledW);
  const minY = Math.min(0, height - scaledH);

  return {
    x: Math.min(0, Math.max(minX, x)),
    y: Math.min(0, Math.max(minY, y)),
  };
}

function fitContainTop(img: HTMLImageElement, tw: number, th: number) {
  const iw = img.naturalWidth || img.width;
  const ih = img.naturalHeight || img.height;

  if (!iw || !ih) return { x: 0, y: 0, width: tw, height: th };

  const s = Math.min(tw / iw, th / ih);
  const rw = iw * s;
  const rh = ih * s;

  return { x: (tw - rw) / 2, y: 0, width: rw, height: rh };
}

function clamp01(value: number) {
  return Math.min(1, Math.max(0, value));
}

export function BodyMapCanvas({
  width,
  height,
  silhouetteUrl,
  points,
  readOnly = false,
  showAnatomicalRegions = false,
  anatomicalRegions = [],
  onStageClick,
  onPointClick,
  onRegionClick,
  stageRef,
  onViewportApi,
}: BodyMapCanvasProps) {
  const [viewport, setViewport] = useState({ scale: 1, x: 0, y: 0 });
  const [silImg, setSilImg] = useState<HTMLImageElement | null>(null);
  const [hoveredRegionId, setHoveredRegionId] = useState<string | null>(null);

  const panningRef = useRef(false);
  const panLastRef = useRef({ x: 0, y: 0 });
  const blockClickRef = useRef(false);

  const setViewportClamped = useCallback(
    (next: { scale: number; x: number; y: number }) => {
      const scale = clampScale(next.scale);
      const pos = scale <= 1 ? { x: 0, y: 0 } : clampPosition(scale, next.x, next.y, width, height);
      setViewport({ scale, x: pos.x, y: pos.y });
    },
    [width, height],
  );

  const viewportRef = useRef(viewport);
  viewportRef.current = viewport;

  useEffect(() => {
    if (!onViewportApi) return;

    const api: BodyMapCanvasApi = {
      zoomIn: () => {
        const v = viewportRef.current;
        setViewportClamped({ scale: v.scale + SCALE_STEP_BUTTON, x: v.x, y: v.y });
      },
      zoomOut: () => {
        const v = viewportRef.current;
        setViewportClamped({ scale: v.scale - SCALE_STEP_BUTTON, x: v.x, y: v.y });
      },
      resetView: () => {
        viewportRef.current = { scale: 1, x: 0, y: 0 };
        setViewport({ scale: 1, x: 0, y: 0 });
      },
    };

    onViewportApi(api);
  }, [onViewportApi, setViewportClamped]);

  useEffect(() => {
    let cancelled = false;
    const img = new window.Image();

    img.crossOrigin = 'anonymous';
    img.src = silhouetteUrl;

    img.onload = () => {
      if (!cancelled) setSilImg(img);
    };

    img.onerror = () => {
      if (!cancelled) setSilImg(null);
    };

    return () => {
      cancelled = true;
    };
  }, [silhouetteUrl]);

  const pointerToWorld = useCallback((stageX: number, stageY: number) => {
    const scale = viewportRef.current.scale;
    const vx = viewportRef.current.x;
    const vy = viewportRef.current.y;

    return {
      x: (stageX - vx) / scale,
      y: (stageY - vy) / scale,
    };
  }, []);

  const getPointerPoint = useCallback(
    (stage: Konva.Stage) => {
      const pointer = stage.getPointerPosition();
      if (!pointer) return null;

      const world = pointerToWorld(pointer.x, pointer.y);

      return {
        x: clamp01(world.x / width),
        y: clamp01(world.y / height),
      };
    },
    [height, pointerToWorld, width],
  );

  const handleWheel = useCallback(
    (e: KonvaEventObject<WheelEvent>) => {
      e.evt.preventDefault();

      const stage = e.target.getStage();
      if (!stage) return;

      const pointer = stage.getPointerPosition();
      if (!pointer) return;

      const oldScale = viewportRef.current.scale;
      const direction = e.evt.deltaY > 0 ? -1 : 1;
      const newScale = clampScale(oldScale + direction * SCALE_STEP_WHEEL);
      const anchor = pointerToWorld(pointer.x, pointer.y);

      if (newScale <= 1) {
        setViewport({ scale: 1, x: 0, y: 0 });
        return;
      }

      const newX = pointer.x - anchor.x * newScale;
      const newY = pointer.y - anchor.y * newScale;
      const pos = clampPosition(newScale, newX, newY, width, height);

      setViewport({ scale: newScale, x: pos.x, y: pos.y });
    },
    [height, pointerToWorld, width],
  );

  useEffect(() => {
    const onMove = (e: MouseEvent) => {
      if (!panningRef.current) return;

      const dx = e.clientX - panLastRef.current.x;
      const dy = e.clientY - panLastRef.current.y;

      panLastRef.current = { x: e.clientX, y: e.clientY };

      if (Math.hypot(dx, dy) > 2) blockClickRef.current = true;

      setViewport((prev) => {
        if (prev.scale <= 1) return prev;

        const next = { ...prev, x: prev.x + dx, y: prev.y + dy };
        const pos = clampPosition(next.scale, next.x, next.y, width, height);
        const v = { scale: prev.scale, x: pos.x, y: pos.y };

        viewportRef.current = v;
        return v;
      });
    };

    const onUp = () => {
      panningRef.current = false;
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);

    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };
  }, [height, width]);

  const handleStageMouseDown = (e: KonvaEventObject<MouseEvent>) => {
    if (!e.evt.altKey) return;
    if (viewportRef.current.scale <= 1) return;

    e.evt.preventDefault();
    panningRef.current = true;
    panLastRef.current = { x: e.evt.clientX, y: e.evt.clientY };
    blockClickRef.current = false;
  };

  const handleBackgroundPointer = (e: KonvaEventObject<MouseEvent | TouchEvent>) => {
    if (readOnly) return;

    if (blockClickRef.current) {
      blockClickRef.current = false;
      return;
    }

    const stage = e.target.getStage();
    if (!stage) return;

    const pointer = stage.getPointerPosition();
    if (!pointer) return;

    const w = pointerToWorld(pointer.x, pointer.y);
    const nx = w.x / width;
    const ny = w.y / height;

    if (nx < 0 || nx > 1 || ny < 0 || ny > 1) return;

    onStageClick(nx, ny);
  };

  const prevDims = useRef<{ w: number; h: number } | null>(null);

  useEffect(() => {
    if (prevDims.current === null) {
      prevDims.current = { w: width, h: height };
      return;
    }

    if (prevDims.current.w !== width || prevDims.current.h !== height) {
      prevDims.current = { w: width, h: height };
      setViewport({ scale: 1, x: 0, y: 0 });
    }
  }, [width, height]);

  const silLayout = silImg ? fitContainTop(silImg, width, height) : null;

  return (
    <Stage
      ref={stageRef}
      width={width}
      height={height}
      onWheel={handleWheel}
      onMouseDown={handleStageMouseDown}
      style={{ cursor: readOnly ? 'default' : 'crosshair' }}
    >
      <Layer>
        <Group x={viewport.x} y={viewport.y} scaleX={viewport.scale} scaleY={viewport.scale}>
          {silLayout && silImg && (
            <KonvaImage
              image={silImg}
              x={silLayout.x}
              y={silLayout.y}
              width={silLayout.width}
              height={silLayout.height}
              listening={false}
            />
          )}

          <Rect
            name="body-hit"
            width={width}
            height={height}
            fill="transparent"
            listening={!readOnly}
            onClick={handleBackgroundPointer}
            onTap={handleBackgroundPointer}
          />

          {showAnatomicalRegions &&
            silLayout &&
            anatomicalRegions.map((region) => {
              const imageBounds = BODY_IMAGE_BOUNDS[region.side];
              const bodyX = silLayout.x + imageBounds.x * silLayout.width;
              const bodyY = silLayout.y + imageBounds.y * silLayout.height;
              const bodyWidth = imageBounds.width * silLayout.width;
              const bodyHeight = imageBounds.height * silLayout.height;

              const x = bodyX + region.x * bodyWidth;
              const y = bodyY + region.y * bodyHeight;
              const regionWidth = region.width * bodyWidth;
              const regionHeight = region.height * bodyHeight;
              const isHovered = hoveredRegionId === region.id;

              const commonRegionProps = {
                fill: '#0ea5e9',
                opacity: isHovered ? 0.24 : 0.10,
                stroke: '#0284c7',
                strokeWidth: isHovered ? 2 : 1,
                dash: [6, 4],
                listening: !readOnly,
                rotation: region.rotation ?? 0,
                onClick: (e: KonvaEventObject<MouseEvent>) => {
                  e.cancelBubble = true;
                  const stage = e.target.getStage();
                  const point = stage ? getPointerPoint(stage) : null;
                  if (point) onRegionClick?.(region, point);
                },
                onTap: (e: KonvaEventObject<TouchEvent>) => {
                  e.cancelBubble = true;
                  const stage = e.target.getStage();
                  const point = stage ? getPointerPoint(stage) : null;
                  if (point) onRegionClick?.(region, point);
                },
                onMouseEnter: (e: KonvaEventObject<MouseEvent>) => {
                  setHoveredRegionId(region.id);
                  const container = e.target.getStage()?.container();
                  if (container && !readOnly) container.style.cursor = 'pointer';
                },
                onMouseLeave: (e: KonvaEventObject<MouseEvent>) => {
                  setHoveredRegionId(null);
                  const container = e.target.getStage()?.container();
                  if (container && !readOnly) container.style.cursor = 'crosshair';
                },
              };

              return (
                <Group key={region.id}>
                  {region.shape === 'ellipse' ? (
                    <Ellipse
                      x={x + regionWidth / 2}
                      y={y + regionHeight / 2}
                      radiusX={regionWidth / 2}
                      radiusY={regionHeight / 2}
                      {...commonRegionProps}
                    />
                  ) : (
                    <Rect
                      x={x}
                      y={y}
                      width={regionWidth}
                      height={regionHeight}
                      cornerRadius={8}
                      {...commonRegionProps}
                    />
                  )}

                  {isHovered && (
                    <Text
                      x={Math.max(0, x - 20)}
                      y={Math.max(4, y - 22)}
                      text={region.name}
                      fontSize={13}
                      fontStyle="bold"
                      fill="#0369a1"
                      width={Math.max(regionWidth + 40, 120)}
                      align="center"
                      listening={false}
                    />
                  )}
                </Group>
              );
            })}

          {points.map((p) => (
            <Circle
              key={p.id}
              x={p.x * width}
              y={p.y * height}
              radius={10}
              fill={SEVERITY_COLORS[p.severity]}
              stroke="white"
              strokeWidth={2}
              shadowColor="black"
              shadowBlur={4}
              shadowOpacity={0.3}
              onClick={(e) => {
                e.cancelBubble = true;
                if (!readOnly) onPointClick(p.id);
              }}
              onTap={(e) => {
                e.cancelBubble = true;
                if (!readOnly) onPointClick(p.id);
              }}
              onMouseEnter={(e) => {
                const container = e.target.getStage()?.container();
                if (container && !readOnly) container.style.cursor = 'pointer';
              }}
              onMouseLeave={(e) => {
                const container = e.target.getStage()?.container();
                if (container && !readOnly) container.style.cursor = 'crosshair';
              }}
            />
          ))}
        </Group>
      </Layer>
    </Stage>
  );
}
