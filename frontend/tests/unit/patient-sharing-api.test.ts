import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
  accessDenied,
  needsSharingRefresh,
  patientSharingApi,
  sharingError,
  sharingStatus,
} from '@/lib/patient-sharing-api';
import { server, apiUrl } from '../mocks/server';
import { ids, plan, record, patientPlan, patientSummary, pageOf } from '../fixtures/sharing';

const errorAt = (status: number, code?: string) => ({
  isAxiosError: true,
  response: { status, data: { code, detail: 'PRIVATE_PROVIDER_DETAIL' } },
});

describe('sharing errors', () => {
  it.each([401, 403, 404])('clears access for HTTP %s', (status) => {
    expect(accessDenied(errorAt(status))).toBe(true);
  });
  it.each([400, 409, 500, 503])('does not classify HTTP %s as revoked access', (status) => {
    expect(accessDenied(errorAt(status))).toBe(false);
  });
  it('handles unknown errors without exposing their contents', () => {
    expect(sharingStatus(new Error('PRIVATE'))).toBeUndefined();
    expect(accessDenied(null)).toBe(false);
    expect(sharingError(new Error('PRIVATE'))).not.toContain('PRIVATE');
    expect(needsSharingRefresh(new Error('Network'))).toBe(true);
  });
  it.each([500, 502, 503])('requires reconciliation after HTTP %s', (status) => {
    expect(needsSharingRefresh(errorAt(status))).toBe(true);
  });
  it('requires reconciliation for an explicit concurrency code', () => {
    expect(needsSharingRefresh(errorAt(409, 'concurrent_modification'))).toBe(true);
  });
  it.each([400, 403, 404, 409])('does not force reconciliation for other HTTP %s errors', (status) => {
    expect(needsSharingRefresh(errorAt(status))).toBe(false);
  });
  it.each([
    'concurrent_modification',
    'patient_archived',
    'patient_account_required',
    'cooperation_not_active',
    'plan_not_publishable',
    'medical_record_not_approved',
    'medical_record_completed_appointment_required',
    'patient_summary_empty',
    'video_not_uploaded',
    'storage_unavailable',
  ])('maps %s to a safe message', (code) => {
    expect(sharingError(errorAt(400, code))).toBeTruthy();
    expect(sharingError(errorAt(400, code))).not.toContain('PRIVATE_PROVIDER_DETAIL');
    expect(sharingError(errorAt(400, code))).not.toContain(code);
  });
  it.each([400, 401, 403, 404, 409, 503, 418])('never exposes raw HTTP %s details', (status) => {
    expect(sharingError(errorAt(status))).not.toContain('PRIVATE_PROVIDER_DETAIL');
  });
});

describe('patient and publication HTTP contracts', () => {
  it.each([
    ['plans', '/therapy-plans'],
    ['summaries', '/visit-summaries'],
  ] as const)('%s sends explicit pagination', async (method, resource) => {
    server.use(
      http.get(apiUrl(`/api/patient-portal${resource}`), ({ request }) => {
        const url = new URL(request.url);
        expect(url.searchParams.get('pageNumber')).toBe('3');
        expect(url.searchParams.get('pageSize')).toBe('10');
        return HttpResponse.json(pageOf([], 3, 3));
      }),
    );
    expect((await patientSharingApi[method](3, new AbortController().signal)).pageNumber).toBe(3);
  });
  it('reads a patient plan through the patient endpoint', async () => {
    server.use(
      http.get(apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}`), () => HttpResponse.json(patientPlan)),
    );
    expect(await patientSharingApi.plan(ids.plan, new AbortController().signal)).toEqual(patientPlan);
  });
  it('reads a summary by record id', async () => {
    server.use(
      http.get(apiUrl(`/api/patient-portal/visit-summaries/${ids.record}`), () =>
        HttpResponse.json(patientSummary),
      ),
    );
    expect(await patientSharingApi.summary(ids.record, new AbortController().signal)).toEqual(patientSummary);
  });
  it('requests playback by patient plan and snapshot item, never library video id', async () => {
    server.use(
      http.get(
        apiUrl(`/api/patient-portal/therapy-plans/${ids.plan}/exercises/${ids.item}/video/playback`),
        () => HttpResponse.json({ url: 'https://video.example.test/signed', expiresAt: publicationExpiry }),
      ),
    );
    const result = await patientSharingApi.playback(ids.plan, ids.item, new AbortController().signal);
    expect(result.expiresAt).toBe(publicationExpiry);
  });
  const publicationExpiry = '2026-11-15T10:05:00Z';
  it.each([
    ['plan', 'publish'],
    ['plan', 'revoke'],
    ['record', 'publish'],
    ['record', 'revoke'],
  ] as const)('%s %s sends only the edit token and selected version', async (kind, action) => {
    const entity = kind === 'plan' ? plan : record;
    const route =
      kind === 'plan'
        ? `/api/therapy-plans/${entity.id}/${action}`
        : `/api/medical-records/${entity.id}/patient-summary/${action}`;
    let body: unknown;
    server.use(
      http.post(apiUrl(route), async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...entity, version: 'new-edit-token' });
      }),
    );
    const signal = new AbortController().signal;
    const result =
      kind === 'plan'
        ? action === 'publish'
          ? await patientSharingApi.publishPlan(plan, 1, signal)
          : await patientSharingApi.revokePlan(plan, signal)
        : action === 'publish'
          ? await patientSharingApi.publishSummary(record, 1, signal)
          : await patientSharingApi.revokeSummary(record, signal);
    expect(body).toEqual(
      action === 'publish' ? { version: entity.version, versionNumber: 1 } : { version: entity.version },
    );
    expect(result.version).toBe('new-edit-token');
  });
  it('does not retry a rejected publication automatically', async () => {
    let calls = 0;
    server.use(
      http.post(apiUrl(`/api/therapy-plans/${ids.plan}/publish`), () => {
        calls++;
        return HttpResponse.json({ code: 'concurrent_modification' }, { status: 409 });
      }),
    );
    await expect(patientSharingApi.publishPlan(plan, 1, new AbortController().signal)).rejects.toMatchObject({
      response: { status: 409 },
    });
    expect(calls).toBe(1);
  });
  it('supports cancelling a request before sending', async () => {
    const controller = new AbortController();
    controller.abort();
    await expect(patientSharingApi.plan(ids.plan, controller.signal)).rejects.toMatchObject({
      code: 'ERR_CANCELED',
    });
  });
});
