'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Plus, Trash2, User } from 'lucide-react';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { BodyMapEditor } from '@/components/body-map/BodyMapEditor';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/primitives';
import { apiClient } from '@/lib/api-client';
import { bodyMapsApi } from '@/lib/api';
import { formatDateTime } from '@/lib/utils';
import type { PatientDetail } from '@/types/api';
import { useAuthStore } from '@/stores/auth-store';

type Mode = 'list' | 'create' | 'view';

export default function PatientPortalBodyMapsPage() {
  const [mode, setMode] = useState<Mode>('list');
  const [selectedMapId, setSelectedMapId] = useState<string | null>(null);
  const [patientId, setPatientId] = useState<string | null>(null);

  const userId = useAuthStore((s) => s.userId);
  const qc = useQueryClient();

  // Portalowy pacjent najpierw musi dostać swój PatientId z backendu.
  // Backend wie kto jest zalogowany, więc przy tworzeniu nie musimy podawać PatientId,
  // ale do GET /bodymapping/patient/{id} musimy mieć id.
  // TODO: dodać endpoint "/api/patientportal/me" który zwraca PatientDetail aktualnego usera.
  // Na razie polegamy że pacjent wpisze/zobaczy swój id - workaround.
  useEffect(() => {
    if (!userId) return;
    // Tymczasowo: zakładamy że patientId = userId przekazany jako query param lub manualnie.
    // W pełnej implementacji: backend endpoint GET /api/patientportal/me
    apiClient
      .get<PatientDetail>('/api/patientportal/me')
      .then((r) => setPatientId(r.data.id))
      .catch(() => {
        // Endpoint jeszcze nie istnieje - feature będzie dostępny po dorobieniu PatientPortal
      });
  }, [userId]);

  const { data: list, isLoading: listLoading } = useQuery({
    queryKey: ['body-maps', patientId],
    queryFn: () => bodyMapsApi.listByPatient(patientId!),
    enabled: mode === 'list' && !!patientId,
  });

  const { data: selectedMap, isLoading: detailLoading } = useQuery({
    queryKey: ['body-map', selectedMapId],
    queryFn: () => bodyMapsApi.getById(selectedMapId!),
    enabled: mode === 'view' && !!selectedMapId,
  });

  const createMutation = useMutation({
    mutationFn: bodyMapsApi.create,
    onSuccess: () => {
      toast.success('Zgłoszono bolesność');
      qc.invalidateQueries({ queryKey: ['body-maps', patientId] });
      setMode('list');
    },
    onError: () => toast.error('Nie udało się zapisać'),
  });

  const deleteMutation = useMutation({
    mutationFn: bodyMapsApi.delete,
    onSuccess: () => {
      toast.success('Usunięto');
      qc.invalidateQueries({ queryKey: ['body-maps', patientId] });
    },
    onError: (e: unknown) => {
      const msg = (e as { response?: { status?: number } })?.response?.status === 403
        ? 'Nie możesz usunąć snapshotu utworzonego przez terapeutę'
        : 'Nie udało się usunąć';
      toast.error(msg);
    },
  });

  if (!patientId) {
    return (
      <div className="p-6">
        <h1 className="text-3xl font-bold">Moje mapy bólu</h1>
        <p className="mt-2 text-muted-foreground">
          Moduł będzie dostępny po tym jak fizjoterapeuta powiąże Twoje konto z kartą pacjenta.
        </p>
      </div>
    );
  }

  if (mode === 'create') {
    return (
      <div className="space-y-4 p-6">
        <Button variant="ghost" size="sm" onClick={() => setMode('list')}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Wróć
        </Button>
        <div>
          <h1 className="text-3xl font-bold">Zgłoś bolesność</h1>
          <p className="text-muted-foreground">
            Zaznacz gdzie i jaki rodzaj bólu odczuwasz. Fizjoterapeuta zobaczy Twoje zgłoszenie.
          </p>
        </div>
        <BodyMapEditor
          saving={createMutation.isPending}
          onSave={(data) => {
            createMutation.mutate({
              // patientId pomijamy - backend weźmie z JWT
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
          <ArrowLeft className="mr-2 h-4 w-4" /> Wróć
        </Button>
        {detailLoading && <p>Ładowanie...</p>}
        {selectedMap && (
          <>
            <div>
              <h1 className="text-3xl font-bold">Podgląd mapy</h1>
              <p className="text-muted-foreground">Zarejestrowano: {formatDateTime(selectedMap.recordedAt)}</p>
            </div>
            <BodyMapEditor initialPoints={selectedMap.painPoints} initialNotes={selectedMap.notes} readOnly />
          </>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">Moje mapy bólu</h1>
          <p className="text-muted-foreground">Historia zgłoszonych bolesności i notatek fizjoterapeuty</p>
        </div>
        <Button onClick={() => setMode('create')}>
          <Plus className="mr-2 h-4 w-4" />
          Zgłoś bolesność
        </Button>
      </div>

      {listLoading && <p className="text-muted-foreground">Ładowanie...</p>}

      {list && list.length === 0 && (
        <Card className="p-8 text-center text-muted-foreground">Brak zgłoszeń. Kliknij &quot;Zgłoś bolesność&quot;.</Card>
      )}

      {list && list.length > 0 && (
        <div className="grid gap-3">
          {list.map((m) => (
            <Card key={m.id} className="flex items-center justify-between p-4 hover:bg-accent/30">
              <button
                type="button"
                onClick={() => { setSelectedMapId(m.id); setMode('view'); }}
                className="flex flex-1 items-center gap-4 text-left"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
                  <User className="h-5 w-5 text-primary" />
                </div>
                <div>
                  <div className="font-medium">{formatDateTime(m.recordedAt)}</div>
                  <div className="text-sm text-muted-foreground">
                    {m.painPointsCount} punktów ·{' '}
                    {m.createdByRole === 'Therapist' ? 'Od fizjoterapeuty' : 'Twoje zgłoszenie'}
                  </div>
                </div>
              </button>
              {m.createdByRole === 'Patient' && (
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => {
                    if (confirm('Usunąć to zgłoszenie?')) deleteMutation.mutate(m.id);
                  }}
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
              )}
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
