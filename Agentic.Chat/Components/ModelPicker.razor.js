// ModelPicker.razor.js
// First ES-module JS isolation usage in this project.
// Two exports: attach a one-shot `pointerdown` listener on `document` that closes
// the popover when the user clicks outside it, and detach that listener on close.
//
// The cleanup value returned from `listenForOutsideClick` is a plain JS function;
// Blazor wraps it as an IJSObjectReference. We pass it back to
// `stopListeningForOutsideClick` when the popover closes from the .NET side
// (Escape key, row selection, trigger toggle). When the user simply clicks
// outside, the listener removes itself and notifies .NET via ClosePopover().

export function listenForOutsideClick(element, dotnetRef) {
    if (!element) return null;
    let active = true;

    const handler = (event) => {
        if (!active) return;
        const target = event.target;
        // `element` is the wrapper that contains both the trigger and the popover.
        // A click anywhere inside it (including the trigger itself) is "inside".
        if (element.contains(target)) return;
        active = false;
        document.removeEventListener('pointerdown', handler);
        // Fire-and-forget; .NET side will tear down its own state.
        dotnetRef.invokeMethodAsync('ClosePopover');
    };

    // `passive: true` so we never block the click from doing its primary job.
    document.addEventListener('pointerdown', handler, { passive: true });

    // Return the cleanup function. Blazor wraps this as IJSObjectReference
    // because the .NET side types the return as IJSObjectReference.
    return function cleanup() {
        if (active) {
            active = false;
            document.removeEventListener('pointerdown', handler);
        }
    };
}

export function stopListeningForOutsideClick(cleanupFn) {
    if (typeof cleanupFn === 'function') {
        cleanupFn();
    }
}
