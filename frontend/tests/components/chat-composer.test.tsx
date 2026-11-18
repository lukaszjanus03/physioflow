import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatComposer } from '@/components/chat/ChatComposer';
import { chatApi } from '@/lib/chat-api';
import { conversation, message } from '../fixtures/chat';
import { ids } from '../fixtures/sharing';

const context = vi.hoisted(() => ({
  owner: '33333333-3333-4333-8333-333333333333',
  setGuard: vi.fn(),
  deny: vi.fn(),
  confirmLeave: (run: () => void) => run(),
}));
vi.mock('@/components/chat/ChatProvider', () => ({ useChat: () => context }));
const accept = vi.fn();
const refresh = vi.fn(async () => {});
beforeEach(() => {
  refresh.mockClear();
  accept.mockClear();
});
function setup(value = conversation) {
  const view = render(<ChatComposer conversation={value} messages={[]} accept={accept} refresh={refresh} />);
  return { ...view, user: userEvent.setup() };
}
describe('chat composer', () => {
  it('disables empty messages and rejects whitespace', async () => {
    const { user } = setup();
    expect(screen.getByRole('button', { name: 'Wyślij wiadomość' })).toBeDisabled();
    await user.type(screen.getByLabelText('Treść wiadomości'), '   ');
    expect(screen.getByRole('button', { name: 'Wyślij wiadomość' })).toBeDisabled();
  });
  it('trims once, sends a UUID and clears only a confirmed message', async () => {
    const send = vi.spyOn(chatApi, 'send').mockResolvedValue(message(1));
    const { user } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), '  hello  ');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    await waitFor(() => expect(accept).toHaveBeenCalled());
    expect(send.mock.calls[0][1]).toEqual({
      requestId: expect.stringMatching(/^[\da-f-]{36}$/i),
      content: 'hello',
    });
    expect(screen.getByLabelText('Treść wiadomości')).toHaveValue('');
  });
  it('retains a frozen body and requestId across uncertain retries', async () => {
    const send = vi
      .spyOn(chatApi, 'send')
      .mockRejectedValueOnce(new Error('SECRET'))
      .mockResolvedValue(message(1));
    const { user } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    const retry = await screen.findByRole('button', { name: 'Ponów tę samą wiadomość' });
    expect(screen.getByLabelText('Treść wiadomości')).toHaveAttribute('readonly');
    expect(screen.getByRole('alert')).not.toHaveTextContent('SECRET');
    await user.click(retry);
    await waitFor(() => expect(send).toHaveBeenCalledTimes(2));
    expect(send.mock.calls[0][1]).toEqual(send.mock.calls[1][1]);
  });
  it('prevents duplicate submissions while a request is pending', async () => {
    let finish!: (value: ReturnType<typeof message>) => void;
    const send = vi.spyOn(chatApi, 'send').mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const { user } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.dblClick(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    expect(send).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Wysyłanie…' })).toBeDisabled();
    await act(async () => finish(message(1)));
  });
  it('shows a readonly explanation without a new-message field', () => {
    setup({ ...conversation, canSend: false, readOnlyReason: 'cooperation_ended' });
    expect(screen.getByRole('status')).toHaveTextContent('zakończona');
    expect(screen.queryByLabelText('Treść wiadomości')).not.toBeInTheDocument();
  });
  it('allows an identical replay after becoming readonly', async () => {
    const send = vi
      .spyOn(chatApi, 'send')
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue(message(1));
    const { user, rerender } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    await screen.findByRole('button', { name: 'Ponów tę samą wiadomość' });
    rerender(
      <ChatComposer
        conversation={{ ...conversation, canSend: false }}
        messages={[]}
        accept={accept}
        refresh={refresh}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Ponów tę samą wiadomość' }));
    expect(send.mock.calls[1][1]).toEqual(send.mock.calls[0][1]);
  });
  it('clears an uncertain send when REST history confirms the same sender/key/content', async () => {
    const send = vi.spyOn(chatApi, 'send').mockRejectedValue(new Error('offline'));
    const { user, rerender } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    await screen.findByRole('button', { name: 'Ponów tę samą wiadomość' });
    const body = send.mock.calls[0][1];
    rerender(
      <ChatComposer
        conversation={conversation}
        messages={[message(1, { ...body, senderId: ids.patient })]}
        accept={accept}
        refresh={refresh}
      />,
    );
    expect(screen.getByLabelText('Treść wiadomości')).toHaveValue('');
  });
  it('blocks idempotency conflicts instead of generating a fresh key', async () => {
    vi.spyOn(chatApi, 'send').mockRejectedValue({
      isAxiosError: true,
      response: { status: 409, data: { code: 'idempotency_key_reused' } },
    });
    const { user } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    expect(await screen.findByRole('button', { name: 'Ponów tę samą wiadomość' })).toBeDisabled();
  });
  it('honors a rate limit while preserving the pending message', async () => {
    vi.spyOn(chatApi, 'send').mockRejectedValue({
      isAxiosError: true,
      response: { status: 429, headers: { 'retry-after': '60' } },
    });
    const { user } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    expect(await screen.findByRole('status')).toHaveTextContent('60 s');
    expect(screen.getByRole('button', { name: 'Ponów tę samą wiadomość' })).toBeDisabled();
  });
  it('removes access on 404 and aborts a pending request on unmount', async () => {
    const send = vi
      .spyOn(chatApi, 'send')
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 404 } });
    const { user, unmount } = setup();
    await user.type(screen.getByLabelText('Treść wiadomości'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Wyślij wiadomość' }));
    await waitFor(() => expect(context.deny).toHaveBeenCalledWith(conversation.id));
    send.mockReturnValue(new Promise(() => {}));
    await user.click(screen.getByRole('button', { name: 'Ponów tę samą wiadomość' }));
    const signal = send.mock.calls.at(-1)?.[2];
    unmount();
    expect(signal?.aborted).toBe(true);
  });
});
