// The phone's chat (Khalil, 22 Oct 2026): the screen follows the chat. Called after a render that added a message, a
// card or a question; it moves the enclosing screen to its end. Nothing is read from the chat and nothing is logged.
export function scrollToEnd(messages) {
    const screen = messages?.closest(".phone-screen") ?? messages?.parentElement;
    if (screen)
        screen.scrollTop = screen.scrollHeight;
}
