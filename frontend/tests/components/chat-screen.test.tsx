import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '@/components/chat/ChatProvider';
import ChatScreen from '@/components/chat/ChatScreen';
import * as api from '@/lib/chat-api';
import { useAuthStore } from '@/stores/auth-store';
import { chatId, conversation, cooperationId, message } from '../fixtures/chat';
import { ids, pageOf } from '../fixtures/sharing';

const nav = vi.hoisted(() => ({ push: vi.fn(), query: '' }));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: nav.push }),
  useSearchParams: () => new URLSearchParams(nav.query),
}));
vi.mock('@/lib/signalr-context', () => ({
  useSignalR: () => ({ isConnected: { chat: false } }),
  useSignalREvent: () => {},
}));
beforeEach(() => {
  nav.query = '';
  useAuthStore.getState().setUser(ids.patient, 'patient@example.test', ['Patient']);
  vi.spyOn(api.chatApi, 'list').mockResolvedValue(pageOf([conversation]));
  vi.spyOn(api.chatApi, 'conversation').mockResolvedValue(conversation);
  vi.spyOn(api.chatApi, 'history').mockImplementation(async (_id, cursor) => ({
    items: cursor.after !== undefined ? [] : [message(1)],
    hasMore: false,
    nextCursor: null,
    lastSequence: 1,
  }));
  vi.spyOn(api, 'chatContacts').mockResolvedValue([
    { cooperationId, name: 'Synthetic Therapist', active: true, patientId: null },
  ]);
});
function open() {
  return render(
    <ChatProvider>
      <ChatScreen role="Patient" />
    </ChatProvider>,
  );
}
describe('chat workspace integration', () => {
  it('opens an active cooperation explicitly and navigates using the returned conversation ID', async () => {
    const post = vi.spyOn(api.chatApi, 'open').mockResolvedValue(conversation);
    open();
    await userEvent.click(await screen.findByRole('button', { name: 'Otwórz rozmowę' }));
    await waitFor(() =>
      expect(nav.push).toHaveBeenCalledWith(`/portal?view=messages&conversation=${chatId}`, {
        scroll: false,
      }),
    );
    expect(post.mock.calls[0][0]).toBe(cooperationId);
  });
  it('loads a selected history and synchronizes on focus', async () => {
    nav.query = `conversation=${chatId}`;
    open();
    expect(await within(await screen.findByRole('log')).findByText('Test message 1')).toBeVisible();
    await act(async () => window.dispatchEvent(new Event('focus')));
    await waitFor(() => expect(api.chatApi.conversation).toHaveBeenCalledTimes(2));
  });
  it('filters the conversation list by the mapped contact name', async () => {
    open();
    await screen.findByRole('button', { name: 'Otwórz rozmowę' });
    await userEvent.type(screen.getByLabelText('Znajdź rozmówcę'), 'no match');
    expect(screen.getByText('Nie znaleziono rozmówcy.')).toBeVisible();
  });
  it('does not request an invalid conversation ID', async () => {
    nav.query = 'conversation=not-a-guid';
    open();
    expect(await screen.findByText('Nieprawidłowy adres rozmowy.')).toBeVisible();
    expect(api.chatApi.conversation).not.toHaveBeenCalled();
  });
  it('clears the entire workspace after a global access denial', async () => {
    vi.mocked(api.chatApi.list).mockRejectedValue({ isAxiosError: true, response: { status: 403 } });
    open();
    expect(await screen.findByRole('button', { name: 'Sprawdź dostęp do wiadomości' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Rozpocznij rozmowę' })).not.toBeInTheDocument();
  });
  it('does not offer ended cooperation as a new conversation', async () => {
    vi.mocked(api.chatContacts).mockResolvedValue([
      { cooperationId, name: 'Synthetic Therapist', active: false, patientId: null },
    ]);
    open();
    expect(await screen.findByText(/Brak aktywnej współpracy w tym kontekście/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Otwórz rozmowę' })).not.toBeInTheDocument();
  });
  it('keeps historical navigation available when contact labels fail', async () => {
    vi.mocked(api.chatContacts).mockRejectedValue(new Error('offline'));
    open();
    expect(await screen.findByRole('button', { name: 'Pobierz współprace ponownie' })).toBeVisible();
    expect(screen.getByRole('button', { name: /Rozmowa · 66666666/ })).toBeVisible();
  });
});
