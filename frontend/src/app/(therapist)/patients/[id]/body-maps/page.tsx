'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Eye, Pencil, Plus, Trash2, User, X } from 'lucide-react';
import { useParams, useRouter } from 'next/navigation';
import { useState } from 'react';
import { toast } from 'sonner';

import { BodyMapEditor } from '@/components/body-map/BodyMapEditor';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/primitives';
import { bodyMapsApi } from '@/lib/api';
import { formatDateTime } from '@/lib/utils';

type Mode = 'list' | 'create' | 'view';

export default function PatientBodyMapsPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const patientId = params.id;
  const qc = useQueryClient();

  const [mode, setMode] = useState<Mode>('list');
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [isEditingSelectedMap, setIsEditingSelectedMap] = useState(false);

  const { data: list, isLoading: listLoading } = useQuery({
    queryKey: ['body-maps', patientId],
    queryFn: () => bodyMapsApi.listByPatient(patientId),
    enabled: mode === 'list',
  });

  const { data: selectedMap, isLoading: detailLoading } = useQuery({
    queryKey: ['body-map', selectedMapId],
    queryFn: () => bodyMapsApi.getById(selectedMapId!),
    enabled: mode === 'view' && !!selectedMapId,
  });

  const createMutation = useMutation({
    mutationFn: bodyMapsApi.create,
    onSuccess: () => {
      toast.success('Mapa bólu zapisana');
      qc.invalidateQueries({ queryKey: ['body-maps', patientId] });
      setMode('list');
    },
    onError: () => toast.error('Nie udało się zapisać mapy'),
  });

  const updateMutation = useMutation({
    mutationFn: ({
      id,
      payload,
    }: {
      id: string;
      payload: Parameters<typeof bodyMapsApi.update>[1];
    }) => bodyMapsApi.update(id, payload),
    onSuccess: async () => {
      toast.success('Mapa bólu zaktualizowana');

      await Promise.all([
        qc.invalidateQueries({ queryKey: ['body-maps', patientId] }),
        qc.invalidateQueries({ queryKey: ['body-map', selectedMapId] }),
      ]);

      setIsEditingSelectedMap(false);
    },
    onError: () => {
      toast.error('Nie udało się zaktualizować mapy. Backend PUT /api/bodymapping/{id} może nie być jeszcze gotowy.');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: bodyMapsApi.delete,
    onSuccess: () => {
      toast.success('Usunięto snapshot');
      qc.invalidateQueries({ queryKey: ['body-maps', patientId] });
    },
    onError: () => toast.error('Nie udało się usunąć'),
  });

  if (mode === 'create') {
    return (
      <div className="space-y-4 p-6">
        <Button variant="ghost" size="sm" onClick={() => setMode('list')}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Wróć do listy
        </Button>

        <div>
          <h1 className="text-3xl font-bold">Nowa mapa bólu</h1>
          <p className="text-muted-foreground">
            Zaznacz punkty bólowe pacjenta na sylwetce.
          </p>
        </div>

        <BodyMapEditor
          saving={createMutation.isPending}
          onSave={(data) => {
            createMutation.mutate({
              patientId,
              notes: data.notes ?? undefined,
              painPoints: data.points.map((p) => ({
                side: p.side,
                x: p.x,
                y: p.y,
                severity: p.severity,
                painScale: p.painScale,
                note: p.note ?? undefined,
              })),
            });
          }}
        />
      </div>
    );
  }

  if (mode === 'view' && selectedMapId) {
    return (
      <div className="space-y-4 p-6">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setMode('list');
              setSelectedMapId(null);
              setIsEditingSelectedMap(false);
            }}
            className="w-fit"
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            Wróć do listy
          </Button>

          {selectedMap && (
            <div className="flex flex-wrap gap-2">
              {isEditingSelectedMap ? (
                <Button
                  variant="outline"
                  onClick={() => setIsEditingSelectedMap(false)}
                  disabled={updateMutation.isPending}
                >
                  <X className="mr-2 h-4 w-4" />
                  Anuluj edycję
                </Button>
              ) : (
                <Button onClick={() => setIsEditingSelectedMap(true)}>
                  <Pencil className="mr-2 h-4 w-4" />
                  Edytuj mapę
                </Button>
              )}
            </div>
          )}
        </div>

        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-3xl font-bold">
              {isEditingSelectedMap ? 'Edycja mapy bólu' : 'Podgląd mapy bólu'}
            </h1>

            <span className="rounded-full bg-muted px-3 py-1 text-xs font-medium text-muted-foreground">
              {isEditingSelectedMap ? 'Tryb edycji' : 'Tryb podglądu'}
            </span>
          </div>

          {selectedMap && (
            <p className="text-muted-foreground">
              Zarejestrowano: {formatDateTime(selectedMap.recordedAt)}
            </p>
          )}
        </div>

        {detailLoading && <p className="text-muted-foreground">Ładowanie...</p>}

        {selectedMap && (
          <BodyMapEditor
            key={`${selectedMap.id}-${isEditingSelectedMap ? 'edit' : 'view'}`}
            initialPoints={selectedMap.painPoints}
            initialNotes={selectedMap.notes}
            readOnly={!isEditingSelectedMap}
            saving={updateMutation.isPending}
            onSave={
              isEditingSelectedMap
                ? (data) => {
                    updateMutation.mutate({
                      id: selectedMap.id,
                      payload: {
                        notes: data.notes ?? undefined,
                        painPoints: data.points.map((p) => ({
                          side: p.side,
                          x: p.x,
                          y: p.y,
                          severity: p.severity,
                          painScale: p.painScale,
                          note: p.note ?? undefined,
                        })),
                      },
                    });
                  }
                : undefined
            }
          />
        )}
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-start gap-3">
          <Button variant="ghost" size="sm" onClick={() => router.push('/patients')}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Pacjenci
          </Button>

          <div>
            <h1 className="text-3xl font-bold">Mapy bólu</h1>
            <p className="text-muted-foreground">Historia snapshotów pacjenta</p>
          </div>
        </div>

        <Button onClick={() => setMode('create')}>
          <Plus className="mr-2 h-4 w-4" />
          Nowa mapa
        </Button>
      </div>

      {listLoading && <p className="text-muted-foreground">Ładowanie...</p>}

      {list && list.length === 0 && (
        <Card className="p-8 text-center text-muted-foreground">
          Brak map bólu dla tego pacjenta. Kliknij &quot;Nowa mapa&quot;, aby utworzyć pierwszą.
        </Card>
      )}

      {list && list.length > 0 && (
        <div className="grid gap-3">
          {list.map((m) => (
            <Card key={m.id} className="flex items-center justify-between p-4 transition-colors hover:bg-accent/30">
              <button
                type="button"
                onClick={() => {
                  setSelectedMapId(m.id);
                  setIsEditingSelectedMap(false);
                  setMode('view');
                }}
                className="flex flex-1 items-center gap-4 text-left"
              >
                <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-primary/10">
                  <User className="h-5 w-5 text-primary" />
                </div>

                <div>
                  <div className="font-medium">{formatDateTime(m.recordedAt)}</div>

                  <div className="text-sm text-muted-foreground">
                    {m.painPointsCount} {getPointCountLabel(m.painPointsCount)} ·{' '}
                    {m.createdByRole === 'Therapist' ? 'Utworzone przez terapeutę' : 'Zgłoszone przez pacjenta'}
                    {m.notes && ` · ${m.notes.slice(0, 60)}${m.notes.length > 60 ? '...' : ''}`}
                  </div>
                </div>
              </button>

              <div className="flex items-center gap-1">
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    setSelectedMapId(m.id);
                    setIsEditingSelectedMap(false);
                    setMode('view');
                  }}
                  aria-label="Podgląd mapy"
                >
                  <Eye className="h-4 w-4" />
                </Button>

                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    setSelectedMapId(m.id);
                    setIsEditingSelectedMap(true);
                    setMode('view');
                  }}
                  aria-label="Edytuj mapę"
                >
                  <Pencil className="h-4 w-4" />
                </Button>

                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    if (confirm('Na pewno usunąć tę mapę bólu?')) {
                      deleteMutation.mutate(m.id);
                    }
                  }}
                  aria-label="Usuń mapę"
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function getPointCountLabel(count: number) {
  if (count === 1) return 'punkt';
  if (count >= 2 && count <= 4) return 'punkty';
  return 'punktów';
}
