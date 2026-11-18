import { Suspense } from 'react';
import ChatScreen from '@/components/chat/ChatScreen';
import { LoadingState } from '@/components/ui/loading-state';

export default function MessagesPage() {
  return (
    <div className="p-4 sm:p-6">
      <Suspense fallback={<LoadingState title="Przygotowywanie wiadomości…" />}>
        <ChatScreen role="Therapist" />
      </Suspense>
    </div>
  );
}
