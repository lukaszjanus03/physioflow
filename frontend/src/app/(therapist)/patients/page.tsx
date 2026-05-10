'use client';

import { useQuery } from '@tanstack/react-query';
import { Plus, Search } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, Input } from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import { formatDate } from '@/lib/utils';

type FilterMode = 'active' | 'all' | 'archived';

export default function PatientsPage() {
  const [search, setSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [filterMode, setFilterMode] = useState<FilterMode>('active');

  const includeArchived = filterMode !== 'active';
  const onlyArchived = filterMode === 'archived';

  const { data, isLoading, isError } = useQuery({
    queryKey: ['patients', { pageNumber, search, includeArchived, onlyArchived }],
    queryFn: () =>
      patientsApi.list({
        pageNumber,
        pageSize: 20,
        search: search || undefined,
        includeArchived,
        onlyArchived,
      }),
  });

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold">Pacjenci</h1>
          <p className="text-muted-foreground">Zarządzaj listą pacjentów</p>
        </div>
        <Button asChild>
          <Link href="/patients/new">
            <Plus className="mr-2 h-4 w-4" />
            Dodaj pacjenta
          </Link>
        </Button>
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative max-w-md flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Szukaj po imieniu, nazwisku, e-mailu..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPageNumber(1);
            }}
            className="pl-9"
          />
        </div>

        <div className="flex gap-2">
          <Button
            type="button"
            size="sm"
            variant={filterMode === 'active' ? 'secondary' : 'outline'}
            onClick={() => {
              setFilterMode('active');
              setPageNumber(1);
            }}
          >
            Aktywni
          </Button>
          <Button
            type="button"
            size="sm"
            variant={filterMode === 'all' ? 'secondary' : 'outline'}
            onClick={() => {
              setFilterMode('all');
              setPageNumber(1);
            }}
          >
            Wszyscy
          </Button>
          <Button
            type="button"
            size="sm"
            variant={filterMode === 'archived' ? 'secondary' : 'outline'}
            onClick={() => {
              setFilterMode('archived');
              setPageNumber(1);
            }}
          >
            Archiwalni
          </Button>
        </div>
      </div>

      <Card>
        {isLoading && <div className="p-8 text-center text-muted-foreground">Ładowanie...</div>}
        {isError && <div className="p-8 text-center text-destructive">Błąd pobierania pacjentów</div>}
        {data && data.items.length === 0 && (
          <div className="p-8 text-center text-muted-foreground">
            Brak pacjentów dla wybranego filtra
          </div>
        )}
        {data && data.items.length > 0 && (
          <div className="divide-y">
            {data.items.map((p) => (
              <Link
                key={p.id}
                href={`/patients/${p.id}`}
                className="flex items-center justify-between p-4 transition-colors hover:bg-accent"
              >
                <div>
                  <div className="flex items-center gap-2">
                    <p className="font-medium">
                      {p.lastName} {p.firstName}
                    </p>
                    {p.isArchived && (
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
                        Zarchiwizowany
                      </span>
                    )}
                  </div>
                  <p className="text-sm text-muted-foreground">
                    {p.email ?? '—'} {p.phoneNumber && `· ${p.phoneNumber}`}
                  </p>
                </div>
                <div className="text-right text-xs text-muted-foreground">
                  Dodano {formatDate(p.createdAt)}
                </div>
              </Link>
            ))}
          </div>
        )}
      </Card>

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-muted-foreground">
            Strona {data.pageNumber} z {data.totalPages} ({data.totalCount} pacjentów)
          </p>
          <div className="space-x-2">
            <Button
              variant="outline"
              size="sm"
              disabled={!data.hasPrevious}
              onClick={() => setPageNumber((p) => p - 1)}
            >
              Poprzednia
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!data.hasNext}
              onClick={() => setPageNumber((p) => p + 1)}
            >
              Następna
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
