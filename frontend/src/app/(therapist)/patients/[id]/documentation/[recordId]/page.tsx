import MedicalRecordEditor from '@/components/medical-records/MedicalRecordEditor';

export default async function RecordPage({
  params,
}: {
  params: Promise<{
    id: string;
    recordId: string;
  }>;
}) {
  const { id, recordId } = await params;

  return (
    <MedicalRecordEditor
      key={`${id}:${recordId}`}
      patientId={id}
      recordId={recordId}
    />
  );
}