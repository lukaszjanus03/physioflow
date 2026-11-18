import TherapistJournal from '@/components/therapist-journal/TherapistJournal';

export default async function PatientJournalPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <TherapistJournal patientId={id} />;
}
