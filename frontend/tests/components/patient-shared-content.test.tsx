import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { delay, http, HttpResponse } from 'msw';
import PatientSharedContent from '@/components/patient-portal/PatientSharedContent';
import { useAuthStore } from '@/stores/auth-store';
import { renderWithQuery } from '../render';
import { apiUrl, server } from '../mocks/server';
import { ids, patientPlan, patientSummary, pageOf } from '../fixtures/sharing';

const navigation = vi.hoisted(() => ({ query: '', push: vi.fn() }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: navigation.push }),
  useSearchParams: () => new URLSearchParams(navigation.query),
}));

describe('patient shared content', () => {
  beforeEach(() => {
    navigation.query = '';
    useAuthStore.getState().setUser(ids.patient, 'patient@example.test', ['Patient']);
    server.use(http.get(apiUrl('/api/patient-accounts/me/cooperations'), () => HttpResponse.json([])));
  });

  it('shows a loading state until the list arrives', async () => {
    server.use(
      http.get(apiUrl('/api/patient-portal/therapy-plans'), async () => {
        await delay(100);
        return HttpResponse.json(pageOf([]));
      }),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    expect(screen.getByText('Pobieranie udostępnionych treści…')).toBeVisible();
    expect(await screen.findByText('Nie masz jeszcze udostępnionych planów')).toBeVisible();
  });

  it.each(['plans', 'summaries'] as const)('explains an empty %s list', async (kind) => {
    const resource = kind === 'plans' ? 'therapy-plans' : 'visit-summaries';
    server.use(http.get(apiUrl(`/api/patient-portal/${resource}`), () => HttpResponse.json(pageOf([]))));
    renderWithQuery(<PatientSharedContent kind={kind} accountId={ids.patient} />);
    expect(
      await screen.findByText(
        kind === 'plans'
          ? 'Nie masz jeszcze udostępnionych planów'
          : 'Nie masz jeszcze udostępnionych podsumowań',
      ),
    ).toBeVisible();
    expect(screen.getByText(/Samo utworzenie planu lub odbycie wizyty nie publikuje/)).toBeVisible();
  });

  it('opens the selected plan without losing list pagination', async () => {
    navigation.query = 'page=3';
    const user = userEvent.setup();
    server.use(
      http.get(apiUrl('/api/patient-portal/therapy-plans'), () =>
        HttpResponse.json(pageOf([{ ...patientPlan, name: patientPlan.content.name }], 3, 3)),
      ),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    await user.click(await screen.findByRole('button', { name: 'Zobacz plan' }));
    expect(navigation.push).toHaveBeenCalledWith(`/portal?view=plans&page=3&content=${ids.plan}`, {
      scroll: true,
    });
  });

  it.each(['-1', '0', 'abc', '1.5'])('normalizes invalid page %s to the first page', async (value) => {
    navigation.query = `page=${value}`;
    let received: string | null = null;
    server.use(
      http.get(apiUrl('/api/patient-portal/therapy-plans'), ({ request }) => {
        received = new URL(request.url).searchParams.get('pageNumber');
        return HttpResponse.json(pageOf([]));
      }),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    await screen.findByText('Nie masz jeszcze udostępnionych planów');
    expect(received).toBe('1');
  });

  it('rejects an invalid content id without fetching its details', async () => {
    navigation.query = 'content=invalid-id';
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Nieprawidłowy adres treści.');
  });

  it('displays only the published snapshot returned by the patient API', async () => {
    navigation.query = `content=${ids.plan}`;
    server.use(
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () => HttpResponse.json(patientPlan)),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    expect(await screen.findByText('Plan testowy - opublikowana wersja 1')).toBeVisible();
    expect(screen.getByText(/udostępniona wersja 1/)).toBeVisible();
    expect(screen.queryByText('Plan testowy - wersja 2')).not.toBeInTheDocument();
  });

  it('renders public summary text, never extra private fields in a response', async () => {
    navigation.query = `content=${ids.record}`;
    server.use(
      http.get(apiUrl(`/api/patient-portal/visit-summaries/${ids.record}`), () =>
        HttpResponse.json({
          ...patientSummary,
          interview: 'PRIVATE_INTERVIEW',
          recommendations: 'PRIVATE_RECOMMENDATIONS',
        }),
      ),
    );
    const { container } = renderWithQuery(<PatientSharedContent kind="summaries" accountId={ids.patient} />);
    expect(await screen.findByText('PUBLIC_SUMMARY_1')).toBeVisible();
    expect(container.textContent).not.toContain('PRIVATE_');
  });

  it('renders summary HTML as inert text', async () => {
    navigation.query = `content=${ids.record}`;
    const text = '<img src=x onerror="alert(1)">';
    server.use(
      http.get(apiUrl(`/api/patient-portal/visit-summaries/${ids.record}`), () =>
        HttpResponse.json({ ...patientSummary, summary: text }),
      ),
    );
    const { container } = renderWithQuery(<PatientSharedContent kind="summaries" accountId={ids.patient} />);
    expect(await screen.findByText(text)).toBeVisible();
    expect(container.querySelector('img')).toBeNull();
  });

  it('removes an open plan after access is revoked', async () => {
    navigation.query = `content=${ids.plan}`;
    const user = userEvent.setup();
    server.use(
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () => HttpResponse.json(patientPlan)),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    await screen.findByText(patientPlan.content.name!);
    server.use(
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () =>
        HttpResponse.json({ detail: 'PRIVATE_DETAIL' }, { status: 404 }),
      ),
    );
    await user.click(screen.getByRole('button', { name: 'Odśwież plan' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/Treść jest niedostępna/));
    expect(screen.queryByText(patientPlan.content.name!)).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).not.toHaveTextContent('PRIVATE_DETAIL');
  });

  it('supports an explicit retry after a transient list error', async () => {
    const user = userEvent.setup();
    server.use(
      http.get(apiUrl('/api/patient-portal/therapy-plans'), () => HttpResponse.json({}, { status: 503 })),
    );
    renderWithQuery(<PatientSharedContent kind="plans" accountId={ids.patient} />);
    await screen.findByRole('alert');
    server.use(http.get(apiUrl('/api/patient-portal/therapy-plans'), () => HttpResponse.json(pageOf([]))));
    await user.click(screen.getByRole('button', { name: 'Pobierz aktualne dane' }));
    expect(await screen.findByText('Nie masz jeszcze udostępnionych planów')).toBeVisible();
  });
});
