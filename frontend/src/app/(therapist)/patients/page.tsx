'use client';

import { useQuery } from '@tanstack/react-query';
import { Plus, Search } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, Input } from '@/components/ui/primitives';
import { patientsApi } from '@/lib/api';
import { formatDate } from '@/lib/utils';

export default function PatientsPage() {
  const [search, setSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);

  const { data, isLoading, isError } = useQuery({
    queryKey: ['patients', { pageNumber, search }],
    queryFn: () => patientsApi.list({ pageNumber, pageSize: 20, search: search || undefined }),
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

      <div className="relative max-w-md">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          placeholder="Szukaj po imieniu, nazwisku, e-mailu..."
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPageNumber(1); }}
          className="pl-9"
        />
      </div>

      <Card>
        {isLoading && <div className="p-8 text-center text-muted-foreground">Ładowanie...</div>}
        {isError && <div className="p-8 text-center text-destructive">Błąd pobierania pacjentów</div>}
        {data && data.items.length === 0 && (
          <div className="p-8 text-center text-muted-foreground">Brak pacjentów</div>
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
                  <p className="font-medium">{p.lastName} {p.firstName}</p>
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
            <Button variant="outline" size="sm" disabled={!data.hasPrevious} onClick={() => setPageNumber((p) => p - 1)}>
              Poprzednia
            </Button>
            <Button variant="outline" size="sm" disabled={!data.hasNext} onClick={() => setPageNumber((p) => p + 1)}>
              Następna
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
