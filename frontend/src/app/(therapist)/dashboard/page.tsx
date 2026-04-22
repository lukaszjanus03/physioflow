'use client';

import { Activity, Calendar, TrendingUp, Users } from 'lucide-react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/primitives';

// Mock data - docelowo GET /api/analytics/dashboard
const mockWeeklyAppointments = [
  { day: 'Pn', count: 8 },
  { day: 'Wt', count: 12 },
  { day: 'Śr', count: 10 },
  { day: 'Cz', count: 14 },
  { day: 'Pt', count: 11 },
  { day: 'Sb', count: 5 },
];

const stats = [
  { label: 'Aktywni pacjenci', value: '42', icon: Users, color: 'text-blue-500' },
  { label: 'Wizyty w tym tygodniu', value: '60', icon: Calendar, color: 'text-green-500' },
  { label: 'Ukończone plany', value: '8', icon: Activity, color: 'text-purple-500' },
  { label: 'Śr. ból pacjentów', value: '4.2', icon: TrendingUp, color: 'text-orange-500' },
];

export default function DashboardPage() {
  return (
    <div className="space-y-6 p-6">
      <div>
        <h1 className="text-3xl font-bold">Dashboard</h1>
        <p className="text-muted-foreground">Przegląd praktyki</p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((s) => {
          const Icon = s.icon;
          return (
            <Card key={s.label}>
              <CardContent className="flex items-center justify-between p-6">
                <div>
                  <p className="text-sm text-muted-foreground">{s.label}</p>
                  <p className="text-3xl font-bold">{s.value}</p>
                </div>
                <Icon className={`h-8 w-8 ${s.color}`} />
              </CardContent>
            </Card>
          );
        })}
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Wizyty w tym tygodniu</CardTitle>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart data={mockWeeklyAppointments}>
              <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
              <XAxis dataKey="day" className="text-xs" />
              <YAxis className="text-xs" />
              <Tooltip />
              <Bar dataKey="count" fill="hsl(var(--primary))" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>
    </div>
  );
}
