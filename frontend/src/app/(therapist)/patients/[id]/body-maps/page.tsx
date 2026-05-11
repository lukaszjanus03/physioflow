'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Plus, Trash2, User } from 'lucide-react';
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

  // Lista snapshotów
  const { data: list, isLoading: listLoading } = useQuery({
    queryKey: ['body-maps', patientId],
    queryFn: () => bodyMapsApi.listByPatient(patientId),
    enabled: mode === 'list',
  });

  // Szczegóły wybranego snapshotu
  const { data: selectedMap, isLoading: detailLoading } = useQuery({
    queryKey: ['body-map', selectedMapId],
    queryFn: () => bodyMapsApi.getById(selectedMapId!),
    enabled: mode === 'view' && !!selectedMapId,
  });

  // Utworzenie
  const createMutation = useMutation({
    mutationFn: bodyMapsApi.create,
    onSuccess: () => {
      toast.success('Mapa bólu zapisana');
      qc.invalidateQueries({ queryKey: ['body-maps', patientId] });
      setMode('list');
    },
    onError: () => toast.error('Nie udało się zapisać mapy'),
  });

  // Usunięcie
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
          <ArrowLeft className="mr-2 h-4 w-4" /> Wróć do listy
        </Button>
        <div>
          <h1 className="text-3xl font-bold">Nowa mapa bólu</h1>
          <p className="text-muted-foreground">Zaznacz punkty bólowe pacjenta na sylwetce.</p>
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
        <Button variant="ghost" size="sm" onClick={() => { setMode('list'); setSelectedMapId(null); }}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Wróć do listy
        </Button>
        <div>
          <h1 className="text-3xl font-bold">Podgląd mapy bólu</h1>
          {selectedMap && (
            <p className="text-muted-foreground">
              Zarejestrowano: {formatDateTime(selectedMap.recordedAt)}
            </p>
          )}
        </div>
        {detailLoading && <p className="text-muted-foreground">Ładowanie...</p>}
        {selectedMap && (
          <BodyMapEditor
            initialPoints={selectedMap.painPoints}
            initialNotes={selectedMap.notes}
            readOnly
          />
        )}
      </div>
    );
  }

  // Lista
  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="sm" onClick={() => router.push('/patients')}>
            <ArrowLeft className="mr-2 h-4 w-4" /> Pacjenci
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
          Brak map bólu dla tego pacjenta. Kliknij &quot;Nowa mapa&quot; aby utworzyć pierwszą.
        </Card>
      )}

      {list && list.length > 0 && (
        <div className="grid gap-3">
          {list.map((m) => (
            <Card key={m.id} className="flex items-center justify-between p-4 transition-colors hover:bg-accent/30">
              <button
                type="button"
                onClick={() => { setSelectedMapId(m.id); setMode('view'); }}
                className="flex flex-1 items-center gap-4 text-left"
              >
                <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-primary/10">
                  <User className="h-5 w-5 text-primary" />
                </div>
                <div>
                  <div className="font-medium">{formatDateTime(m.recordedAt)}</div>
                  <div className="text-sm text-muted-foreground">
                    {m.painPointsCount} punkt{m.painPointsCount === 1 ? '' : m.painPointsCount < 5 ? 'y' : 'ów'} ·{' '}
                    {m.createdByRole === 'Therapist' ? 'Utworzone przez terapeutę' : 'Zgłoszone przez pacjenta'}
                    {m.notes && ` · ${m.notes.slice(0, 60)}${m.notes.length > 60 ? '...' : ''}`}
                  </div>
                </div>
              </button>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => {
                  if (confirm('Na pewno usunąć tę mapę bólu?')) {
                    deleteMutation.mutate(m.id);
                  }
                }}
              >
                <Trash2 className="h-4 w-4 text-destructive" />
              </Button>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
