// Keeps a streaming response in view until the reader deliberately scrolls away.
// The controller owns its DOM-only state so streaming render updates do not need a
// Blazor round trip to show or hide the jump control.

const BOTTOM_THRESHOLD = 48;

export function initialize(container, sentinel, jumpButton) {
    if (!container || !sentinel || !jumpButton) return null;

    let isStreaming = false;
    let isFollowing = true;
    let frame = 0;

    const isAtBottom = () =>
        container.scrollHeight - container.scrollTop - container.clientHeight <= BOTTOM_THRESHOLD;

    const updateJumpButton = () => {
        jumpButton.hidden = !isStreaming || isFollowing;
    };

    const scrollToLatest = () => {
        frame = 0;
        if (!isFollowing) return;

        // The app's default smooth scrolling is pleasant for user-initiated
        // navigation, but it falls behind streamed deltas. Bypass it here.
        const originalBehavior = container.style.scrollBehavior;
        container.style.scrollBehavior = "auto";
        container.scrollTop = container.scrollHeight;
        container.style.scrollBehavior = originalBehavior;
    };

    const scheduleScrollToLatest = () => {
        if (!isFollowing || frame) return;
        frame = requestAnimationFrame(scrollToLatest);
    };

    const handleScroll = () => {
        if (isAtBottom()) {
            isFollowing = true;
        } else if (isStreaming) {
            isFollowing = false;
        }
        updateJumpButton();
    };

    const observer = new IntersectionObserver(
        ([entry]) => {
            if (entry.isIntersecting) {
                isFollowing = true;
                updateJumpButton();
            }
        },
        {
            root: container,
            rootMargin: `0px 0px ${BOTTOM_THRESHOLD}px 0px`,
            threshold: 1,
        });

    const follow = () => {
        isFollowing = true;
        updateJumpButton();
        scheduleScrollToLatest();
    };

    const jumpToLatest = () => follow();

    container.addEventListener("scroll", handleScroll, { passive: true });
    jumpButton.addEventListener("click", jumpToLatest);
    observer.observe(sentinel);
    updateJumpButton();

    return {
        update(streaming) {
            isStreaming = streaming;
            if (isStreaming) scheduleScrollToLatest();
            updateJumpButton();
        },
        follow,
        dispose() {
            if (frame) cancelAnimationFrame(frame);
            container.removeEventListener("scroll", handleScroll);
            jumpButton.removeEventListener("click", jumpToLatest);
            observer.disconnect();
        },
    };
}
