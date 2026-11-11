export async function withMinimumLoading<T>(
  operation: () => Promise<T>,
  enabled = true,
): Promise<T> {
  if (!enabled) {
    return operation();
  }

  const minimumTime = new Promise<void>((resolve) => {
    setTimeout(resolve, 450);
  });

  const [result] = await Promise.all([
    operation(),
    minimumTime,
  ]);

  return result;
}