export function openModal(dialog, trigger, dotnetRef, closeMethod, focusSelector) {
    const onKeyDown = event => {
        if (event.key === "Escape") {
            event.preventDefault();
            dotnetRef.invokeMethodAsync(closeMethod);
            return;
        }
        if (event.key !== "Tab") return;
        const focusable = [...dialog.querySelectorAll(
            "button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex='-1'])")];
        if (!focusable.length) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    };
    document.addEventListener("keydown", onKeyDown);
    dialog.showModal?.();
    dialog.querySelector(focusSelector)?.focus();
    return () => {
        document.removeEventListener("keydown", onKeyDown);
        dialog.close?.();
        trigger?.focus();
    };
}

export function closeModal(cleanupFn, restoreFocus = true) {
    cleanupFn?.();
    if (!restoreFocus && document.activeElement instanceof HTMLElement) {
        document.activeElement.blur();
    }
}

export function listenForOutsideClick(element, dotnetRef) {
    const listener = event => {
        if (!element.contains(event.target)) {
            dotnetRef.invokeMethodAsync("CloseFromOutsideClick");
        }
    };
    setTimeout(() => document.addEventListener("pointerdown", listener), 0);
    return () => document.removeEventListener("pointerdown", listener);
}

export function stopListeningForOutsideClick(cleanupFn) {
    cleanupFn?.();
}

export function positionPopover(wrapper) {
    const popover = wrapper.querySelector(".model-picker-popover");
    const trigger = wrapper.querySelector(".model-picker-trigger");
    if (!popover || !trigger) return;
    const rect = trigger.getBoundingClientRect();
    const margin = 8;
    const width = Math.min(860, window.innerWidth - margin * 2);
    popover.style.width = `${width}px`;
    popover.style.left = `${Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin))}px`;
    popover.style.top = `${Math.min(rect.bottom + margin, window.innerHeight - 120)}px`;
}

export function listenForPopoverReposition(wrapper) {
    const listener = () => positionPopover(wrapper);
    window.addEventListener("resize", listener);
    window.addEventListener("scroll", listener, true);
    listener();
    return () => {
        window.removeEventListener("resize", listener);
        window.removeEventListener("scroll", listener, true);
    };
}

export function stopPopoverReposition(cleanupFn) {
    cleanupFn?.();
}

export function shouldAutoFocusSearch() {
    return matchMedia("(pointer: fine)").matches;
}

export function scrollActiveModelIntoView() {
    document.querySelector(".model-picker-row.is-active")
        ?.scrollIntoView({ block: "nearest" });
}
