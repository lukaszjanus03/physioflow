import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  test: {
    environment: 'jsdom',
    environmentOptions: { jsdom: { url: 'http://localhost:5000' } },
    setupFiles: ['./tests/setup.ts'],
    include: ['tests/**/*.test.{ts,tsx}'],
    clearMocks: true,
    restoreMocks: true,
    maxWorkers: 2,
    env: { NEXT_PUBLIC_API_URL: 'http://localhost:5000' },
    coverage: {
      provider: 'v8',
      include: [
        'src/lib/analytics-api.ts',
        'src/components/analytics/AnalyticsPanels.tsx',
        'src/lib/patient-export-api.ts',
        'src/components/patients/PatientExportDialog.tsx',
        'src/components/patients/PatientCardPdf.tsx',
        'src/lib/chat-api.ts',
        'src/lib/chat-messages.ts',
        'src/lib/chat-thread.ts',
        'src/lib/signalr-context.tsx',
        'src/components/chat/**/*.{ts,tsx}',
        'src/lib/patient-sharing-api.ts',
        'src/hooks/use-patient-content.ts',
        'src/components/patient-sharing/PatientPublication.tsx',
        'src/components/patient-portal/PatientSharedContent.tsx',
        'src/lib/exercise-journal-api.ts',
        'src/lib/exercise-journal-form.ts',
        'src/components/patient-journal/**/*.{ts,tsx}',
        'src/lib/therapist-journal-api.ts',
        'src/components/therapist-journal/**/*.{ts,tsx}',
        'src/app/(therapist)/layout.tsx',
      ],
      reporter: ['text', 'html', 'lcov'],
    },
  },
});
