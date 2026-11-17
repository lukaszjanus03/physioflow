import { describe, expect, it } from 'vitest';
import {
  journalForm,
  journalHref,
  journalPayload,
  validJournalDate,
  validateJournal,
  validateJournalFilters,
  warsawDate,
} from '@/lib/exercise-journal-form';
import { journalEntry } from '../fixtures/journal';

const published = '2026-11-15T23:30:00Z';
const today = '2026-11-17';
const base = () => journalForm(undefined, today);

describe('Warsaw calendar dates', () => {
  it.each([
    ['2026-11-16T23:30:00Z', '2026-11-17'],
    ['2026-06-16T22:30:00Z', '2026-06-17'],
    ['2026-03-29T00:30:00Z', '2026-03-29'],
    ['2026-03-29T01:30:00Z', '2026-03-29'],
    ['2026-10-25T00:30:00Z', '2026-10-25'],
    ['2026-10-25T01:30:00Z', '2026-10-25'],
  ])('formats %s as %s without shifting a date-only value', (input, expected) => {
    expect(warsawDate(new Date(input))).toBe(expected);
  });
  it.each(['2026-02-30', '2025-02-29', '2026-13-01', '1899-12-31', '2026-1-2', 'invalid', ''])(
    'rejects invalid date %s',
    (value) => {
      expect(validJournalDate(value)).toBe(false);
    },
  );
  it.each(['1900-01-01', '2024-02-29', '2026-11-17'])('accepts valid date %s', (value) => {
    expect(validJournalDate(value)).toBe(true);
  });
});

describe('journal validation and report payload', () => {
  it('allows an optional empty dose, pain and comment', () => {
    expect(validateJournal(base(), published, today)).toEqual({});
    expect(journalPayload(base())).toEqual({
      performedOn: today,
      status: 'Completed',
      actualSets: null,
      actualRepetitions: null,
      actualDurationSeconds: null,
      painLevel: null,
      comment: null,
    });
  });
  it('preserves reported zero pain instead of treating it as missing', () => {
    expect(journalPayload({ ...base(), painLevel: '0' }).painLevel).toBe(0);
    expect(journalForm({ ...journalEntry, painLevel: 0 }).painLevel).toBe('0');
    expect(journalForm(journalEntry).painLevel).toBe('');
  });
  it.each(['2026-11-15', '2026-11-18', ''])(
    'rejects a performance date outside publication/today: %s',
    (performedOn) => {
      expect(validateJournal({ ...base(), performedOn }, published, today).performedOn).toBeTruthy();
    },
  );
  it('uses Warsaw publication date, not UTC date', () => {
    expect(validateJournal({ ...base(), performedOn: '2026-11-16' }, published, today)).toEqual({});
  });
  it.each([
    ['actualSets', '0'],
    ['actualSets', '101'],
    ['actualSets', '1.5'],
    ['actualRepetitions', '1001'],
    ['actualRepetitions', '-1'],
    ['actualDurationSeconds', '86401'],
    ['painLevel', '11'],
    ['painLevel', '-1'],
    ['painLevel', '0.5'],
  ] as const)('rejects %s=%s', (key, value) => {
    expect(validateJournal({ ...base(), [key]: value }, published, today)[key]).toBeTruthy();
  });
  it.each([
    ['actualSets', '100'],
    ['actualRepetitions', '1000'],
    ['actualDurationSeconds', '86400'],
    ['painLevel', '0'],
    ['painLevel', '10'],
  ] as const)('accepts boundary %s=%s', (key, value) => {
    expect(validateJournal({ ...base(), [key]: value }, published, today)).toEqual({});
  });
  it('rejects repetitions and duration together', () => {
    expect(
      validateJournal({ ...base(), actualRepetitions: '1', actualDurationSeconds: '1' }, published, today)
        .actualRepetitions,
    ).toBeTruthy();
  });
  it('does not allow a skipped exercise to contain an actual dose', () => {
    expect(
      validateJournal({ ...base(), status: 'Skipped', actualSets: '1' }, published, today).status,
    ).toBeTruthy();
    expect(
      validateJournal(
        { ...base(), status: 'Skipped', painLevel: '0', comment: 'Powód pominięcia' },
        published,
        today,
      ),
    ).toEqual({});
  });
  it('validates the raw UTF-16 length before trimming', () => {
    expect(validateJournal({ ...base(), comment: '😀'.repeat(1000) }, published, today)).toEqual({});
    expect(validateJournal({ ...base(), comment: '😀'.repeat(1001) }, published, today).comment).toBeTruthy();
    expect(validateJournal({ ...base(), comment: ' '.repeat(2001) }, published, today).comment).toBeTruthy();
  });
  it('normalizes the comment only in the report payload', () => {
    expect(journalPayload({ ...base(), comment: '  Test  ' }).comment).toBe('Test');
    expect(journalPayload({ ...base(), comment: '  ' }).comment).toBeNull();
  });
});

describe('journal filters', () => {
  const filters = { from: '', to: '', status: '' as const, therapyPlanId: '' };
  it('allows future dates as filter boundaries', () => {
    expect(validateJournalFilters({ ...filters, from: '2099-01-01', to: '2100-01-01' })).toBeNull();
  });
  it('rejects reversed dates and invalid plan identifiers', () => {
    expect(validateJournalFilters({ ...filters, from: '2026-11-17', to: '2026-11-16' })).toBeTruthy();
    expect(validateJournalFilters({ ...filters, therapyPlanId: 'not-a-uuid' })).toBeTruthy();
  });
  it('retains filters when moving between pages', () => {
    const href = journalHref({ ...filters, from: '2026-11-01', status: 'Partial' }, 3);
    expect(href).toBe('/portal?view=journal&page=3&from=2026-11-01&status=Partial');
  });
});
