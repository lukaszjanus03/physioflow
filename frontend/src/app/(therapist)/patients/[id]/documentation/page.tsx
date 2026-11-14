import MedicalRecordList from '@/components/medical-records/MedicalRecordList';

export default async function DocumentationPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{
    appointmentId?: string | string[];
  }>;
}) {
  const { id } = await params;
  const search = await searchParams;

  const appointmentId = Array.isArray(
    search.appointmentId,
  )
    ? 'invalid'
    : search.appointmentId ?? '';

  return (
    <MedicalRecordList
      key={`${id}:${appointmentId}`}
      patientId={id}
      appointmentId={appointmentId}
    />
  );
}