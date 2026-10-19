// The phone's copy button (milestone 9b): writes the text to the clipboard. Nothing is logged.
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
