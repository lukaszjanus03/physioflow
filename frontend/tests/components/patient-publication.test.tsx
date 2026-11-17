import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { delay, http, HttpResponse } from 'msw';
import PatientPublicationPanel, { PublicationBadge } from '@/components/patient-sharing/PatientPublication';
import { useAuthStore } from '@/stores/auth-store';
import { renderWithQuery } from '../render';
import { apiUrl, server } from '../mocks/server';
import { ids, plan, planVersions, record, recordVersions, publication } from '../fixtures/sharing';

const callbacks = () => ({ onUpdated: vi.fn(), onBusyChange: vi.fn(), onConflict: vi.fn() });
function context(linked = true, archived = false) {
  server.use(
    http.get(apiUrl(`/api/patients/${ids.patient}`), () =>
      HttpResponse.json({ id: ids.patient, isLinkedToAccount: linked, isArchived: archived }),
    ),
    http.get(apiUrl(`/api/therapy-plans/${ids.plan}/versions`), () => HttpResponse.json(planVersions)),
    http.get(apiUrl(`/api/medical-records/${ids.record}/versions`), () => HttpResponse.json(recordVersions)),
  );
}
async function ready() {
  await screen.findByRole('combobox', { name: 'Wersja przeznaczona dla pacjenta' });
}

describe('therapist publication panel', () => {
  beforeEach(() => {
    useAuthStore.getState().setUser(ids.therapist, 'therapist@example.test', ['Therapist']);
    context();
  });

  it('requires a preview and explicit confirmation before sending', async () => {
    const user = userEvent.setup();
    const props = callbacks();
    let calls = 0;
    let sent: unknown;
    server.use(
      http.post(apiUrl(`/api/therapy-plans/${ids.plan}/publish`), async ({ request }) => {
        calls++;
        sent = await request.json();
        return HttpResponse.json({ ...plan, version: 'rotated-token', publication });
      }),
    );
    renderWithQuery(
      <PatientPublicationPanel kind="plan" entity={plan} busy={false} locked={false} canPublish {...props} />,
    );
    await ready();
    expect(calls).toBe(0);
    await user.selectOptions(screen.getByRole('combobox'), '1');
    await user.click(screen.getByRole('button', { name: 'Podgląd i udostępnienie' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Plan testowy - wersja 1')).toBeVisible();
    expect(calls).toBe(0);
    await user.click(within(dialog).getByRole('button', { name: 'Udostępnij tę wersję' }));
    await waitFor(() =>
      expect(props.onUpdated).toHaveBeenCalledWith(expect.objectContaining({ version: 'rotated-token' })),
    );
    expect(sent).toEqual({ version: plan.version, versionNumber: 1 });
    expect(calls).toBe(1);
    expect(props.onBusyChange).toHaveBeenCalledWith(true);
    expect(props.onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('cancels a preview without publishing', async () => {
    const user = userEvent.setup();
    const props = callbacks();
    renderWithQuery(
      <PatientPublicationPanel kind="plan" entity={plan} busy={false} locked={false} canPublish {...props} />,
    );
    await ready();
    await user.click(screen.getByRole('button', { name: 'Podgląd i udostępnienie' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Anuluj' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(props.onUpdated).not.toHaveBeenCalled();
    expect(props.onBusyChange).not.toHaveBeenCalled();
  });

  it.each([
    { linked: false, archived: false, locked: false, canPublish: true },
    { linked: true, archived: true, locked: false, canPublish: true },
    { linked: true, archived: false, locked: true, canPublish: true },
    { linked: true, archived: false, locked: false, canPublish: false },
  ])('blocks publication when prerequisites fail: %j', async ({ linked, archived, locked, canPublish }) => {
    context(linked, archived);
    renderWithQuery(
      <PatientPublicationPanel
        kind="plan"
        entity={plan}
        busy={false}
        locked={locked}
        canPublish={canPublish}
        {...callbacks()}
      />,
    );
    await ready();
    expect(screen.getByRole('button', { name: 'Podgląd i udostępnienie' })).toBeDisabled();
  });

  it('previews only the selected public summary, never private record fields', async () => {
    const user = userEvent.setup();
    renderWithQuery(
      <PatientPublicationPanel
        kind="record"
        entity={record}
        busy={false}
        locked={false}
        canPublish
        {...callbacks()}
      />,
    );
    await ready();
    await user.selectOptions(screen.getByRole('combobox'), '1');
    await user.click(screen.getByRole('button', { name: 'Podgląd i udostępnienie' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('PUBLIC_SUMMARY_1')).toBeVisible();
    expect(dialog.textContent).not.toContain('PUBLIC_SUMMARY_2');
    expect(dialog.textContent).not.toContain('PRIVATE_');
  });

  it('blocks publishing a saved version with an empty summary', async () => {
    server.use(
      http.get(apiUrl(`/api/medical-records/${ids.record}/versions`), () =>
        HttpResponse.json([{ ...recordVersions[0], content: { ...record.content, patientSummary: '  ' } }]),
      ),
    );
    renderWithQuery(
      <PatientPublicationPanel
        kind="record"
        entity={record}
        busy={false}
        locked={false}
        canPublish
        {...callbacks()}
      />,
    );
    await ready();
    expect(screen.getByRole('button', { name: 'Podgląd i udostępnienie' })).toBeDisabled();
  });

  it('can revoke a previous publication even for an archived card', async () => {
    context(true, true);
    const user = userEvent.setup();
    const props = callbacks();
    const entity = { ...plan, publication, version: 'fresh-token' };
    let sent: unknown;
    server.use(
      http.post(apiUrl(`/api/therapy-plans/${ids.plan}/revoke`), async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ ...entity, publication: null, version: 'revoked-token' });
      }),
    );
    renderWithQuery(
      <PatientPublicationPanel
        kind="plan"
        entity={entity}
        busy={false}
        locked={false}
        canPublish={false}
        {...props}
      />,
    );
    await ready();
    await user.click(screen.getByRole('button', { name: 'Cofnij udostępnienie' }));
    await user.click(screen.getByRole('button', { name: 'Potwierdzam cofnięcie' }));
    await waitFor(() => expect(props.onUpdated).toHaveBeenCalled());
    expect(sent).toEqual({ version: 'fresh-token' });
  });

  it('does not retry a conflict and asks the parent to reconcile', async () => {
    const user = userEvent.setup();
    const props = callbacks();
    let calls = 0;
    server.use(
      http.post(apiUrl(`/api/therapy-plans/${ids.plan}/publish`), () => {
        calls++;
        return HttpResponse.json(
          { code: 'concurrent_modification', detail: 'PRIVATE_SERVER_DETAIL' },
          { status: 409 },
        );
      }),
    );
    renderWithQuery(
      <PatientPublicationPanel kind="plan" entity={plan} busy={false} locked={false} canPublish {...props} />,
    );
    await ready();
    await user.click(screen.getByRole('button', { name: 'Podgląd i udostępnienie' }));
    await user.click(screen.getByRole('button', { name: 'Udostępnij tę wersję' }));
    await waitFor(() => expect(props.onConflict).toHaveBeenCalledTimes(1));
    expect(calls).toBe(1);
    expect(props.onUpdated).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).not.toHaveTextContent('PRIVATE_SERVER_DETAIL');
    expect(screen.getByRole('button', { name: 'Podgląd i udostępnienie' })).toBeDisabled();
    expect(props.onBusyChange).toHaveBeenLastCalledWith(false);
  });

  it('blocks another publication while a request is pending', async () => {
    const user = userEvent.setup();
    const props = callbacks();
    server.use(
      http.post(apiUrl(`/api/therapy-plans/${ids.plan}/publish`), async () => {
        await delay(150);
        return HttpResponse.json({ ...plan, publication });
      }),
    );
    renderWithQuery(
      <PatientPublicationPanel kind="plan" entity={plan} busy={false} locked={false} canPublish {...props} />,
    );
    await ready();
    await user.click(screen.getByRole('button', { name: 'Podgląd i udostępnienie' }));
    await user.click(screen.getByRole('button', { name: 'Udostępnij tę wersję' }));
    expect(screen.getByRole('button', { name: 'Podgląd i udostępnienie' })).toBeDisabled();
    await waitFor(() => expect(props.onUpdated).toHaveBeenCalledTimes(1));
  });

  it('explains when the published version is older than the current draft', async () => {
    renderWithQuery(
      <PatientPublicationPanel
        kind="plan"
        entity={{ ...plan, publication }}
        busy={false}
        locked={false}
        canPublish
        {...callbacks()}
      />,
    );
    await ready();
    expect(screen.getByText(/opublikowana treść pozostaje w wersji 1/)).toBeVisible();
  });

  it('shows a badge for an outdated account link without claiming full access', () => {
    renderWithQuery(<PublicationBadge publication={{ ...publication, isAccessible: false }} />);
    expect(screen.getByText(/nieaktualne powiązanie konta/)).toBeVisible();
  });
});
