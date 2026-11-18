import type { ChatMessage } from './chat-api';

export function mergeChatMessages(previous: ChatMessage[], incoming: ChatMessage[]): ChatMessage[] {
  const byId = new Map(previous.map((m) => [m.id, m]));
  for (const message of incoming) {
    const old = byId.get(message.id);
    byId.set(message.id, old ? { ...message, readAt: old.readAt ?? message.readAt } : message);
  }
  return [...byId.values()].sort((a, b) => a.sequence - b.sequence);
}
// A response or hint ahead of a gap never advances the synchronization cursor.
export function continuousChatSequence(messages: ChatMessage[], floor: number): number {
  const sequences = new Set(messages.map((m) => m.sequence));
  let cursor = floor;
  while (sequences.has(cursor + 1)) cursor++;
  return cursor;
}
export function validChatCursor(value: number | null): value is number {
  return value !== null && Number.isSafeInteger(value) && value >= 0;
}
