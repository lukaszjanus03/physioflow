import type { ChatConversation, ChatMessage } from '@/lib/chat-api';
import { ids } from './sharing';

export const chatId = '66666666-6666-4666-8666-666666666666';
export const cooperationId = '77777777-7777-4777-8777-777777777777';
export const conversation: ChatConversation = {
  id: chatId,
  cooperationId,
  patientUserId: ids.patient,
  therapistUserId: ids.therapist,
  canSend: true,
  readOnlyReason: null,
  lastMessage: null,
  unreadCount: 0,
  lastSequence: 0,
  readThrough: 0,
  version: 'v1',
};
export function message(sequence: number, overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: `88888888-8888-4888-8888-${String(sequence).padStart(12, '0')}`,
    conversationId: chatId,
    sequence,
    senderId: ids.therapist,
    recipientId: ids.patient,
    requestId: `99999999-9999-4999-8999-${String(sequence).padStart(12, '0')}`,
    content: `Test message ${sequence}`,
    sentAt: '2026-11-18T12:30:00.123456Z',
    readAt: null,
    ...overrides,
  };
}
