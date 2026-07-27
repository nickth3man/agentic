// ModelPicker.razor.js
// First ES-module JS isolation usage in this project.
// Exports: outside-click close, viewport-clamped popover positioning, and
// small helpers for search autofocus / active-row scroll.
//
// The cleanup value returned from `listenForOutsideClick` /
// `listenForPopoverReposition` is a plain JS function; Blazor wraps it as an
// IJSObjectReference. We pass it back to the matching stop* helper when the
// popover closes from the .NET side (Escape key, row selection, trigger toggle).

const POPOVER_MAX_WIDTH = 640;
const POPOVER_MARGIN = 8;
const POPOVER_GAP = 8;

export function openModal(dialog, trigger, dotnetRef, closeMethod, focusSelector) {
    if (!(dialog instanceof HTMLDialogElement)) return null;

    let active = true;
    let closing = false;

    const cleanup = (restoreFocus = true) => {
        if (!active) return;
        active = false;
        dialog.removeEventListener("cancel", handleCancel);
        dialog.removeEventListener("click", handleBackdropClick);
        if (dialog.open) dialog.close();
        if (restoreFocus && trigger instanceof HTMLElement) {
            trigger.focus({ preventScroll: true });
        }
    };

    const requestClose = () => {
        if (!active || closing) return;
        closing = true;
        cleanup(true);
        dotnetRef.invokeMethodAsync(closeMethod);
    };

    const handleCancel = (event) => {
        event.preventDefault();
        requestClose();
    };

    const handleBackdropClick = (event) => {
        if (event.target !== dialog) return;
        const rect = dialog.getBoundingClientRect();
        const inside =
            event.clientX >= rect.left &&
            event.clientX <= rect.right &&
            event.clientY >= rect.top &&
            event.clientY <= rect.bottom;
        if (!inside) requestClose();
    };

    dialog.addEventListener("cancel", handleCancel);
    dialog.addEventListener("click", handleBackdropClick);
    if (dialog.open) dialog.close();
    dialog.showModal();

    requestAnimationFrame(() => {
        if (!active) return;
        const preferred = focusSelector ? dialog.querySelector(focusSelector) : null;
        const target = preferred instanceof HTMLElement ? preferred : dialog;
        target.focus({ preventScroll: true });
    });

    return cleanup;
}

export function closeModal(cleanupFn, restoreFocus = true) {
    if (typeof cleanupFn === "function") {
        cleanupFn(restoreFocus);
    }
}

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

/**
 * Clamp the popover to the viewport: fixed position, width <= 640px, never
 * past the left/right/bottom edges. Absolute left:0 + width:100vw was wrong
 * because left is relative to the trigger, not the viewport.
 *
 * If an ancestor still has a transform/filter (fixed containing block), style
 * offsets are relative to that ancestor — subtract its viewport origin.
 */
export function positionPopover(wrapper) {
    if (!wrapper) return;
    const trigger = wrapper.querySelector('.model-picker-trigger');
    const popover = wrapper.querySelector('.model-picker-popover');
    if (!trigger || !popover) return;

    const viewportWidth = window.visualViewport?.width ?? window.innerWidth;
    const viewportHeight = window.visualViewport?.height ?? window.innerHeight;
    const triggerRect = trigger.getBoundingClientRect();
    const origin = getFixedContainingBlockOrigin(popover);

    if (window.matchMedia("(max-width: 640px)").matches) {
        popover.style.position = "fixed";
        popover.style.inset = "0";
        popover.style.left = "0";
        popover.style.top = "0";
        popover.style.right = "0";
        popover.style.width = `${viewportWidth}px`;
        popover.style.maxWidth = `${viewportWidth}px`;
        popover.style.height = `${viewportHeight}px`;
        popover.style.maxHeight = `${viewportHeight}px`;

        const tableWrap = popover.querySelector(".model-picker-table-wrap");
        if (tableWrap instanceof HTMLElement) {
            const searchRow = popover.querySelector(".model-picker-search-row");
            const chromeHeight = searchRow instanceof HTMLElement
                ? searchRow.getBoundingClientRect().height
                : 0;
            tableWrap.style.maxHeight = `${Math.max(0, viewportHeight - chromeHeight)}px`;
        }
        return;
    }

    const width = Math.min(POPOVER_MAX_WIDTH, Math.max(0, viewportWidth - POPOVER_MARGIN * 2));
    const maxLeft = viewportWidth - POPOVER_MARGIN - width;
    const leftViewport = Math.min(
        Math.max(triggerRect.left, POPOVER_MARGIN),
        Math.max(POPOVER_MARGIN, maxLeft));

    // Apply horizontal placement first so height measurement uses the final width.
    popover.style.position = 'fixed';
    popover.style.inset = 'auto';
    popover.style.left = `${leftViewport - origin.left}px`;
    popover.style.right = 'auto';
    popover.style.width = `${width}px`;
    popover.style.maxWidth = `${width}px`;
    popover.style.height = 'auto';
    popover.style.maxHeight = 'none';

    // Keep the scrollable table within the remaining viewport height.
    // Do not force a floor height — a short visual viewport (mobile keyboard)
    // must be allowed to shrink the table so both edges stay on-screen.
    const tableWrap = popover.querySelector('.model-picker-table-wrap');
    let topViewport = triggerRect.bottom + POPOVER_GAP;
    // Clear prior max-height so measurement reflects natural size first.
    if (tableWrap instanceof HTMLElement) {
        tableWrap.style.maxHeight = '';
    }
    const chromeHeight = popover.getBoundingClientRect().height
        - (tableWrap instanceof HTMLElement ? tableWrap.getBoundingClientRect().height : 0);
    const availableForTable = Math.max(
        0,
        viewportHeight - POPOVER_MARGIN - topViewport - Math.max(0, chromeHeight));
    if (tableWrap instanceof HTMLElement) {
        tableWrap.style.maxHeight = `${availableForTable}px`;
    }

    const popoverHeight = popover.getBoundingClientRect().height;
    const maxTop = viewportHeight - POPOVER_MARGIN - popoverHeight;
    if (popoverHeight > 0 && topViewport > maxTop) {
        topViewport = Math.max(POPOVER_MARGIN, maxTop);
    }

    popover.style.top = `${topViewport - origin.top}px`;
}

/** True when this element establishes a containing block for position:fixed. */
function createsFixedContainingBlock(style) {
    if (style.transform !== 'none') return true;
    if (style.filter && style.filter !== 'none') return true;
    if (style.perspective && style.perspective !== 'none') return true;
    if (style.backdropFilter && style.backdropFilter !== 'none') return true;
    // Safari still exposes the prefixed form in some builds.
    if (style.webkitBackdropFilter && style.webkitBackdropFilter !== 'none') return true;

    const contain = style.contain || '';
    if (/\b(layout|paint|strict|content)\b/.test(contain)) return true;

    const willChange = style.willChange || '';
    if (/\b(transform|filter|perspective|backdrop-filter|contain)\b/.test(willChange)) {
        return true;
    }

    return false;
}

/** Viewport origin of the nearest ancestor that traps position:fixed. */
function getFixedContainingBlockOrigin(el) {
    let node = el.parentElement;
    while (node && node !== document.documentElement) {
        if (createsFixedContainingBlock(getComputedStyle(node))) {
            const rect = node.getBoundingClientRect();
            return { left: rect.left, top: rect.top };
        }
        node = node.parentElement;
    }
    return { left: 0, top: 0 };
}

/**
 * Position once, then reclamp on resize / window scroll / visualViewport
 * changes while open. Nested table scrolling is intentionally ignored so we
 * do not clear max-height mid-scroll (which clamps scrollTop and jumps the
 * list). Returns a cleanup function (same pattern as listenForOutsideClick).
 */
export function listenForPopoverReposition(wrapper) {
    if (!wrapper) return null;

    let scheduled = false;
    let rafId = 0;
    const reposition = () => {
        if (scheduled) return;
        scheduled = true;
        rafId = requestAnimationFrame(() => {
            scheduled = false;
            rafId = 0;
            positionPopover(wrapper);
        });
    };

    reposition();

    window.addEventListener('resize', reposition, { passive: true });
    // No capture — only window/document scroll, not the model table scroller.
    window.addEventListener('scroll', reposition, { passive: true });

    const visualViewport = window.visualViewport;
    if (visualViewport) {
        // Mobile keyboard / pinch-zoom update visualViewport without a window resize.
        visualViewport.addEventListener('resize', reposition, { passive: true });
        visualViewport.addEventListener('scroll', reposition, { passive: true });
    }

    return function cleanup() {
        window.removeEventListener('resize', reposition);
        window.removeEventListener('scroll', reposition);
        if (visualViewport) {
            visualViewport.removeEventListener('resize', reposition);
            visualViewport.removeEventListener('scroll', reposition);
        }
        if (rafId) {
            cancelAnimationFrame(rafId);
            rafId = 0;
        }
        scheduled = false;
    };
}

export function stopPopoverReposition(cleanupFn) {
    if (typeof cleanupFn === 'function') {
        cleanupFn();
    }
}

// Focusing a search box on a touch-first device opens the virtual keyboard and
// obscures most of the picker. Fine-pointer desktops retain the fast keyboard flow.
export function shouldAutoFocusSearch() {
    return !window.matchMedia('(any-pointer: coarse)').matches &&
        navigator.maxTouchPoints === 0;
}

export function scrollActiveModelIntoView() {
    document.querySelector('.model-picker-row.is-active')
        ?.scrollIntoView({ block: 'nearest' });
}
