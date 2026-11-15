import TherapyPlanList from '@/components/therapy/TherapyPlanList';

export default async function PlansPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  return (
    <TherapyPlanList
      key={id}
      patientId={id}
    />
  );
}