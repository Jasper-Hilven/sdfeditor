const isEditable = target =>
    target instanceof HTMLElement &&
    (target.isContentEditable || ["INPUT", "SELECT", "TEXTAREA"].includes(target.tagName));

export function listenToKeys(dotNetHandler) {
    const onKeyDown = event => {
        if (event.repeat || event.ctrlKey || event.altKey || event.metaKey || isEditable(event.target))
            return;
        if (event.key.length === 1 || event.key === "Enter")
            event.preventDefault();
        dotNetHandler.invokeMethodAsync("OnKeyDown", event.key);
    };
    document.addEventListener("keydown", onKeyDown);
    return { dispose: () => document.removeEventListener("keydown", onKeyDown) };
}

