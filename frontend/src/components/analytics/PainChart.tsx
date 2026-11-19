'use client';

import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { formatPain, painChartDays, type PainTrend } from '@/lib/analytics-api';

export default function PainChart({ data }: { data: PainTrend }) {
  const days = painChartDays(data);
  return (
    <div
      role="img"
      aria-label="Wykres dziennych ocen bólu. Dokładne wartości w tabeli poniżej."
      className="h-64 min-w-0 rounded-xl border bg-muted/15 pt-4 sm:h-72"
    >
      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
        <LineChart data={days} margin={{ top: 8, right: 22, left: 0, bottom: 8 }}>
          <CartesianGrid vertical={false} stroke="hsl(var(--border))" />
          <XAxis
            dataKey="date"
            tickFormatter={(date: string) => date.slice(5).split('-').reverse().join('.')}
            minTickGap={18}
            padding={{ left: 10, right: 10 }}
            tick={{ fontSize: 11 }}
          />
          <YAxis width={34} domain={[0, 10]} ticks={[0, 2, 4, 6, 8, 10]} tick={{ fontSize: 11 }} />
          <Tooltip
            content={({ active, label }) => {
              const point = active ? days.find(({ date }) => date === label) : undefined;
              if (!point || point.averagePainLevel === null) return null;
              return (
                <div className="rounded-lg border bg-card p-3 text-xs shadow-md">
                  <p>{point.date.split('-').reverse().join('.')}</p>
                  <p className="mt-1 font-semibold">Średnia: {formatPain(point.averagePainLevel)} / 10</p>
                  <p>Liczba ocen: {point.ratingCount}</p>
                </div>
              );
            }}
          />
          <Line
            dataKey="averagePainLevel"
            type="linear"
            connectNulls={false}
            stroke="hsl(var(--primary))"
            strokeWidth={2.5}
            dot={{ r: days.length > 100 ? 2 : 3, fill: 'hsl(var(--primary))' }}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
