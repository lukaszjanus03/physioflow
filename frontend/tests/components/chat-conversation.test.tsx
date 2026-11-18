import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatConversationPane } from '@/components/chat/ChatConversationPane';
import { conversation, message } from '../fixtures/chat';
import { ids } from '../fixtures/sharing';

const actions = vi.hoisted(() => ({
  markVisible: vi.fn(),
  refresh: vi.fn(),
  older: vi.fn(),
  accept: vi.fn(),
}));
vi.mock('@/components/chat/use-chat-thread', () => ({
  useChatThread: () => ({
    ...actions,
    conversation,
    messages: [message(1), message(2, { senderId: ids.patient, recipientId: ids.therapist })],
    loading: false,
    busy: false,
    error: null,
    revoked: false,
    hasOlder: false,
    change: 'initial',
  }),
}));
vi.mock('@/components/chat/ChatComposer', () => ({ ChatComposer: () => <div>composer</div> }));
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const bottom = this.hasAttribute('data-message-id') ? 150 : 500;
    return {
      top: 100,
      bottom,
      height: bottom - 100,
      width: 400,
      left: 0,
      right: 400,
      x: 0,
      y: 100,
      toJSON: () => ({}),
    };
  });
  vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
  vi.spyOn(document, 'hasFocus').mockReturnValue(true);
});
function open() {
  render(
    <ChatConversationPane id={conversation.id} owner={ids.patient} name="Test contact" back={vi.fn()} />,
  );
  fireEvent.scroll(screen.getByRole('log'));
}
describe('visible incoming-message receipts', () => {
  it('never acknowledges messages in a minimized chat window', () => {
    render(
      <ChatConversationPane
        id={conversation.id}
        owner={ids.patient}
        name="Test contact"
        back={vi.fn()}
        compact
        active={false}
      />,
    );
    fireEvent.scroll(screen.getByRole('log'));
    fireEvent.focus(window);
    expect(actions.markVisible).not.toHaveBeenCalled();
  });
  it('acknowledges the highest visible incoming ID, never a later own message', () => {
    open();
    expect(actions.markVisible).toHaveBeenCalledWith(message(1));
    expect(actions.markVisible.mock.calls.every(([item]) => item.recipientId === ids.patient)).toBe(true);
  });
  it('does not acknowledge in a hidden tab', () => {
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
    open();
    expect(actions.markVisible).not.toHaveBeenCalled();
  });
  it('does not acknowledge without focus', () => {
    vi.spyOn(document, 'hasFocus').mockReturnValue(false);
    open();
    expect(actions.markVisible).not.toHaveBeenCalled();
  });
  it('does not acknowledge a message outside the visible viewport', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      const top = this.hasAttribute('data-message-id') ? 600 : 100;
      return {
        top,
        bottom: top + 50,
        height: 50,
        width: 400,
        left: 0,
        right: 400,
        x: 0,
        y: top,
        toJSON: () => ({}),
      };
    });
    open();
    expect(actions.markVisible).not.toHaveBeenCalled();
  });
  it('does not infer a read receipt for own messages from cursor or event state', () => {
    open();
    expect(screen.getByText(/Wysłano/)).toBeVisible();
    expect(screen.queryByText(/Przeczytano/)).not.toBeInTheDocument();
  });
});
