// Forwards the page's key presses to .NET as KeyboardEvent.key, the layout-dependent character.
// Presses while typing in a form field or with Ctrl/Alt/Meta held are left to the browser.

const isEditable = target =>
    target instanceof HTMLElement &&
    (target.isContentEditable || ["INPUT", "SELECT", "TEXTAREA"].includes(target.tagName));

export function listenToKeys(dotNetHandler) {
    const onKeyDown = event => {
        if (event.repeat || event.ctrlKey || event.altKey || event.metaKey || isEditable(event.target))
            return;
        dotNetHandler.invokeMethodAsync("OnKeyDown", event.key);
    };
    document.addEventListener("keydown", onKeyDown);
    return { dispose: () => document.removeEventListener("keydown", onKeyDown) };
}
