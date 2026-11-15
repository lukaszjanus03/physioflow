import TherapyPlanEditor from '@/components/therapy/TherapyPlanEditor';

export default async function PlanPage({
  params,
}: {
  params: Promise<{
    id: string;
    planId: string;
  }>;
}) {
  const { id, planId } = await params;

  return (
    <TherapyPlanEditor
      key={`${id}:${planId}`}
      patientId={id}
      planId={planId}
    />
  );
}