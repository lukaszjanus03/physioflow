import { act, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useEffect } from 'react';
import { ChatProvider, ChatUnreadBadge, useChat } from '@/components/chat/ChatProvider';
import { chatApi } from '@/lib/chat-api';
import { useAuthStore } from '@/stores/auth-store';
import { conversation } from '../fixtures/chat';
import { ids, pageOf } from '../fixtures/sharing';

const hub = vi.hoisted(() => ({
  connected: false,
  events: new Map<string, (value: { conversationId: string }) => void>(),
}));
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock('@/lib/signalr-context', () => ({
  useSignalR: () => ({ isConnected: { chat: hub.connected } }),
  useSignalREvent: (_hub: string, event: string, handler: (value: { conversationId: string }) => void) => {
    useEffect(() => {
      hub.events.set(event, handler);
      return () => {
        hub.events.delete(event);
      };
    }, [event, handler]);
  },
}));
function Probe() {
  const chat = useChat();
  return (
    <>
      <ChatUnreadBadge />
      <p>{chat.owner ?? 'guest'}</p>
      <p>{chat.error ? 'error' : 'ok'}</p>
      <button onClick={() => chat.deny(conversation.id)}>deny</button>
      <button onClick={() => void chat.refresh()}>refresh</button>
    </>
  );
}
function mount() {
  useAuthStore.getState().setUser(ids.patient, 'patient@example.test', ['Patient']);
  return render(
    <ChatProvider>
      <Probe />
    </ChatProvider>,
  );
}
describe('global chat state', () => {
  it('uses server counters, not duplicated SignalR hint counts', async () => {
    vi.spyOn(chatApi, 'list').mockResolvedValue(pageOf([{ ...conversation, unreadCount: 3 }]));
    mount();
    expect(await screen.findByLabelText('Nieprzeczytane wiadomości: 3')).toBeVisible();
    act(() => {
      for (let i = 0; i < 3; i++) hub.events.get('MessageCreated')?.({ conversationId: conversation.id });
    });
    await waitFor(() => expect(chatApi.list).toHaveBeenCalledTimes(3));
    expect(screen.getByLabelText('Nieprzeczytane wiadomości: 3')).toBeVisible();
  });
  it('clears counters on access loss and on logout', async () => {
    vi.spyOn(chatApi, 'list').mockResolvedValue(pageOf([{ ...conversation, unreadCount: 3 }]));
    mount();
    await screen.findByLabelText('Nieprzeczytane wiadomości: 3');
    vi.mocked(chatApi.list).mockRejectedValue({ isAxiosError: true, response: { status: 403 } });
    await act(async () => screen.getByText('refresh', { exact: true }).click());
    await screen.findByText('error', { exact: true });
    expect(screen.queryByLabelText(/Nieprzeczytane wiadomości/)).not.toBeInTheDocument();
    act(() => useAuthStore.getState().logout());
    expect(screen.getByText('guest')).toBeVisible();
  });
  it('does not resurrect a denied conversation during list refresh', async () => {
    vi.spyOn(chatApi, 'list').mockResolvedValue(pageOf([{ ...conversation, unreadCount: 2 }]));
    mount();
    await screen.findByLabelText('Nieprzeczytane wiadomości: 2');
    act(() => screen.getByText('deny', { exact: true }).click());
    await act(async () => screen.getByText('refresh', { exact: true }).click());
    expect(screen.queryByLabelText(/Nieprzeczytane wiadomości/)).not.toBeInTheDocument();
  });
});
