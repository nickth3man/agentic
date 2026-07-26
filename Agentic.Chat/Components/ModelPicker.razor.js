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

    const width = Math.min(POPOVER_MAX_WIDTH, Math.max(0, viewportWidth - POPOVER_MARGIN * 2));
    const maxLeft = viewportWidth - POPOVER_MARGIN - width;
    const leftViewport = Math.min(
        Math.max(triggerRect.left, POPOVER_MARGIN),
        Math.max(POPOVER_MARGIN, maxLeft));

    // Apply horizontal placement first so height measurement uses the final width.
    popover.style.position = 'fixed';
    popover.style.left = `${leftViewport - origin.left}px`;
    popover.style.right = 'auto';
    popover.style.width = `${width}px`;
    popover.style.maxWidth = `${width}px`;

    // Keep the scrollable table within the remaining viewport height.
    const tableWrap = popover.querySelector('.model-picker-table-wrap');
    let topViewport = triggerRect.bottom + POPOVER_GAP;
    // Clear prior max-height so measurement reflects natural size first.
    if (tableWrap instanceof HTMLElement) {
        tableWrap.style.maxHeight = '';
    }
    const chromeHeight = popover.getBoundingClientRect().height
        - (tableWrap instanceof HTMLElement ? tableWrap.getBoundingClientRect().height : 0);
    const availableForTable = Math.max(
        120,
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

/** Viewport origin of the nearest ancestor that traps position:fixed. */
function getFixedContainingBlockOrigin(el) {
    let node = el.parentElement;
    while (node && node !== document.documentElement) {
        const style = getComputedStyle(node);
        if (style.transform !== 'none'
            || (style.filter && style.filter !== 'none')
            || (style.perspective && style.perspective !== 'none')) {
            const rect = node.getBoundingClientRect();
            return { left: rect.left, top: rect.top };
        }
        node = node.parentElement;
    }
    return { left: 0, top: 0 };
}

/**
 * Position once, then reclamp on resize/scroll while open. Returns a cleanup
 * function (same pattern as listenForOutsideClick).
 */
export function listenForPopoverReposition(wrapper) {
    if (!wrapper) return null;

    const reposition = () => positionPopover(wrapper);
    reposition();

    window.addEventListener('resize', reposition, { passive: true });
    // Capture scroll so nested scrollers (table wrap) and page scroll both reclamp.
    window.addEventListener('scroll', reposition, { passive: true, capture: true });

    return function cleanup() {
        window.removeEventListener('resize', reposition);
        window.removeEventListener('scroll', reposition, true);
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
