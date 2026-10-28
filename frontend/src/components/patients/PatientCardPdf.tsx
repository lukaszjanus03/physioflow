'use client';

import {
  Document,
  Font,
  Page,
  StyleSheet,
  Text,
  View,
  pdf,
} from '@react-pdf/renderer';

import type { PatientDetail } from '@/types/api';

Font.register({
  family: 'PhysioFlowPdf',
  fonts: [
    {
      src: '/fonts/NotoSans-Regular.ttf',
      fontWeight: 400,
    },
    {
      src: '/fonts/NotoSans-Bold.ttf',
      fontWeight: 700,
    },
  ],
});

const colors = {
  primary: '#0f9f91',
  primaryDark: '#08766d',
  primaryLight: '#edf8f6',
  text: '#162c35',
  muted: '#647786',
  border: '#d8e5e9',
  white: '#ffffff',
};

const styles = StyleSheet.create({
  page: {
    paddingTop: 88,
    paddingBottom: 64,
    paddingHorizontal: 36,
    fontFamily: 'PhysioFlowPdf',
    fontSize: 11,
    color: colors.text,
    backgroundColor: colors.white,
  },

  header: {
    position: 'absolute',
    top: 24,
    left: 36,
    right: 36,
    height: 47,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    borderBottomWidth: 1.5,
    borderBottomColor: colors.primary,
  },
  headerLeft: {
    width: '60%',
  },
  headerRight: {
    width: '38%',
    alignItems: 'flex-end',
  },
  brand: {
    fontSize: 15,
    fontWeight: 700,
    color: colors.primaryDark,
  },
  headerPatient: {
    marginTop: 3,
    fontSize: 8,
    color: colors.muted,
  },
  documentLabel: {
    fontSize: 8,
    fontWeight: 700,
    color: colors.primaryDark,
    textAlign: 'right',
  },
  generatedAt: {
    marginTop: 4,
    fontSize: 7.5,
    color: colors.muted,
    textAlign: 'right',
  },

  hero: {
    paddingVertical: 11,
    paddingHorizontal: 14,
    marginBottom: 13,
    backgroundColor: colors.primaryLight,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 7,
  },
  eyebrow: {
    marginBottom: 4,
    fontSize: 8,
    fontWeight: 700,
    color: colors.primaryDark,
  },
  patientName: {
    fontSize: 20,
    fontWeight: 700,
    lineHeight: 1.25,
  },
  status: {
    marginTop: 5,
    fontSize: 9,
    color: colors.muted,
  },

  columns: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    marginBottom: 9,
  },
  leftCard: {
    width: '49%',
    marginRight: '2%',
  },
  rightCard: {
    width: '49%',
  },
  card: {
    padding: 12,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 7,
  },
  cardTitle: {
    marginBottom: 8,
    fontSize: 11,
    fontWeight: 700,
    color: colors.primaryDark,
  },
  field: {
    marginBottom: 7,
  },
  label: {
    marginBottom: 2,
    fontSize: 8,
    color: colors.muted,
  },
  value: {
    fontSize: 11,
    lineHeight: 1.4,
  },
  recordMetadata: {
    marginBottom: 17,
    fontSize: 7.5,
    lineHeight: 1.5,
    color: colors.muted,
  },

  sectionTitle: {
    marginBottom: 12,
    paddingBottom: 6,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
    fontSize: 13,
    fontWeight: 700,
    color: colors.primaryDark,
  },
  medicalSection: {
    marginBottom: 15,
  },
  medicalTitle: {
    marginBottom: 7,
    paddingLeft: 8,
    borderLeftWidth: 3,
    borderLeftColor: colors.primary,
    fontSize: 11,
    fontWeight: 700,
  },
  medicalText: {
    paddingLeft: 11,
    fontSize: 11,
    lineHeight: 1.5,
  },
  nextParagraph: {
    marginTop: 8,
  },

  footerRule: {
    position: 'absolute',
    bottom: 45,
    left: 36,
    right: 36,
    height: 1,
    backgroundColor: colors.border,
  },
  footerNote: {
    position: 'absolute',
    bottom: 25,
    left: 36,
    width: 390,
    fontSize: 7,
    color: colors.muted,
  },
  footerPageNumber: {
    position: 'absolute',
    bottom: 25,
    right: 36,
    width: 90,
    fontSize: 8,
    textAlign: 'right',
    color: colors.muted,
  },
});

interface PatientCardPdfProps {
  patient: PatientDetail;
  generatedAt: Date;
}

function displayText(value: string | null | undefined): string {
  const text = value?.trim();

  if (!text) return 'Brak informacji';

  return text.replace(/\S{30,}/gu, (word) =>
    Array.from(word)
      .reduce<string[]>((chunks, character, index) => {
        const chunkIndex = Math.floor(index / 24);

        chunks[chunkIndex] =
          (chunks[chunkIndex] ?? '') + character;

        return chunks;
      }, [])
      .join('\u200b')
  );
}

function formatBirthDate(value: string | null): string {
  if (!value) return 'Brak informacji';

  const date = new Date(value);

  if (Number.isNaN(date.getTime())) return 'Brak informacji';

  return new Intl.DateTimeFormat('pl-PL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(date);
}

function formatTimestamp(value: string | Date | null): string {
  if (!value) return 'Brak informacji';

  const date = value instanceof Date ? value : new Date(value);

  if (Number.isNaN(date.getTime())) return 'Brak informacji';

  return new Intl.DateTimeFormat('pl-PL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Europe/Warsaw',
  }).format(date);
}

function shortenHeaderName(value: string): string {
  const normalized = value.replace(/\s+/gu, ' ').trim();

  return normalized.length > 58
    ? `${normalized.slice(0, 55)}...`
    : normalized;
}

function splitMedicalText(value: string | null) {
  const text =
    value?.replace(/\r\n?/gu, '\n').trim() || 'Brak informacji';

  const paragraphs = text
    .split(/\n[ \t]*\n+/gu)
    .map((paragraph) => paragraph.trim())
    .filter(Boolean);

  const firstParagraph = paragraphs[0];
  const maxIntroLength = 180;

  if (firstParagraph.length <= maxIntroLength) {
    return {
      intro: firstParagraph,
      remaining: paragraphs.slice(1).join('\n\n'),
      continuesParagraph: false,
    };
  }

  let splitAt = firstParagraph.lastIndexOf(' ', maxIntroLength);

  if (splitAt < 1) {
    splitAt = maxIntroLength;
  }

  return {
    intro: firstParagraph.slice(0, splitAt).trimEnd(),
    remaining: [
      firstParagraph.slice(splitAt).trimStart(),
      ...paragraphs.slice(1),
    ]
      .filter(Boolean)
      .join('\n\n'),
    continuesParagraph: true,
  };
}

function Field({
  label,
  value,
}: {
  label: string;
  value: string | null | undefined;
}) {
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <Text style={styles.value}>{displayText(value)}</Text>
    </View>
  );
}

function MedicalSection({
  title,
  value,
}: {
  title: string;
  value: string | null;
}) {
  const { intro, remaining, continuesParagraph } =
    splitMedicalText(value);

  return (
    <View style={styles.medicalSection}>
      {/* Tytuł i krótki początek tekstu zawsze pozostają razem. */}
      <View wrap={false}>
        <Text style={styles.medicalTitle}>{title}</Text>

        <Text style={styles.medicalText}>
          {displayText(intro)}
        </Text>
      </View>

      {remaining && (
        <Text
          style={
            continuesParagraph
              ? styles.medicalText
              : [styles.medicalText, styles.nextParagraph]
          }
          orphans={3}
          widows={3}
        >
          {displayText(remaining)}
        </Text>
      )}
    </View>
  );
}

export function PatientCardPdf({
  patient,
  generatedAt,
}: PatientCardPdfProps) {
  const fullName =
    `${patient.firstName} ${patient.lastName}`.trim();

  const documentReference = patient.id
    .slice(0, 8)
    .toUpperCase();

  return (
    <Document
      title="Karta pacjenta"
      author="PhysioFlow"
      subject="Dane pacjenta i informacje medyczne"
      creator="PhysioFlow"
      language="pl-PL"
    >
      <Page size="A4" style={styles.page} wrap>
        {/* Elementy powtarzane umieszczamy przed treścią dokumentu. */}
        <View style={styles.header} fixed>
          <View style={styles.headerLeft}>
            <Text style={styles.brand}>PhysioFlow</Text>

            <Text style={styles.headerPatient}>
              {shortenHeaderName(fullName)}
            </Text>
          </View>

          <View style={styles.headerRight}>
            <Text style={styles.documentLabel}>
              KARTA PACJENTA
            </Text>

            <Text style={styles.generatedAt}>
              {formatTimestamp(generatedAt)}
            </Text>
          </View>
        </View>

        <View style={styles.footerRule} fixed />

        <Text style={styles.footerNote} fixed>
          Dane osobowe i medyczne | Karta: {documentReference}
        </Text>

        <Text
          style={styles.footerPageNumber}
          fixed
          render={({ pageNumber, totalPages }) =>
            `Strona ${pageNumber} z ${totalPages ?? ''}`
          }
        />

        <View style={styles.hero} wrap={false}>
          <Text style={styles.eyebrow}>DANE PACJENTA</Text>

          <Text style={styles.patientName}>
            {displayText(fullName)}
          </Text>

          <Text style={styles.status}>
            Status: {patient.isArchived ? 'Archiwalny' : 'Aktywny'}
          </Text>
        </View>

        <View style={styles.columns}>
          <View style={[styles.card, styles.leftCard]}>
            <Text style={styles.cardTitle}>Dane osobowe</Text>

            <Field label="PESEL" value={patient.pesel} />

            <Field
              label="Data urodzenia"
              value={formatBirthDate(patient.dateOfBirth)}
            />
          </View>

          <View style={[styles.card, styles.rightCard]}>
            <Text style={styles.cardTitle}>Kontakt</Text>

            <Field label="E-mail" value={patient.email} />
            <Field label="Telefon" value={patient.phoneNumber} />
            <Field label="Adres" value={patient.address} />
          </View>
        </View>

        <Text style={styles.recordMetadata}>
          Dodano do systemu: {formatTimestamp(patient.createdAt)}
          {'   |   '}
          Aktualizacja: {formatTimestamp(patient.updatedAt)}
        </Text>

        <Text
          style={styles.sectionTitle}
          minPresenceAhead={110}
        >
          Informacje medyczne
        </Text>

        <MedicalSection
          title="Historia medyczna"
          value={patient.medicalHistory}
        />

        <MedicalSection
          title="Aktualne leki"
          value={patient.medications}
        />

        <MedicalSection
          title="Notatki ogólne"
          value={patient.generalNotes}
        />
      </Page>
    </Document>
  );
}

export async function downloadPatientCardPdf(
  patient: PatientDetail
): Promise<void> {
  const generatedAt = new Date();

  const blob = await pdf(
    <PatientCardPdf
      patient={patient}
      generatedAt={generatedAt}
    />
  ).toBlob();

  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');

  try {
    link.href = url;
    link.download = `karta-pacjenta-${patient.id}.pdf`;

    document.body.appendChild(link);
    link.click();
  } finally {
    link.remove();

    window.setTimeout(() => {
      window.URL.revokeObjectURL(url);
    }, 60_000);
  }
}