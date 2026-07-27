export function openDrawer(drawer, main, trigger, dotnetRef) {
    const previous = document.activeElement;
    const selector = "button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex='-1'])";
    const onKeyDown = event => {
        if (event.key === "Escape") {
            event.preventDefault();
            dotnetRef.invokeMethodAsync("CloseSidebarFromJs");
            return;
        }
        if (event.key !== "Tab") return;
        const focusable = [...drawer.querySelectorAll(selector)];
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
    drawer.querySelector(selector)?.focus();
    return {
        dispose(restoreFocus = true) {
            document.removeEventListener("keydown", onKeyDown);
            if (restoreFocus) (trigger ?? previous)?.focus?.();
            main?.removeAttribute("aria-hidden");
        }
    };
}

export function initialize(container, sentinel, jumpButton) {
    let streaming = false;
    let following = true;
    const nearBottom = () =>
        container.scrollHeight - container.scrollTop - container.clientHeight < 96;
    const render = () => { jumpButton.hidden = !streaming || following; };
    const follow = () => {
        following = true;
        sentinel.scrollIntoView({ block: "end" });
        render();
    };
    const onScroll = () => {
        following = nearBottom();
        render();
    };
    container.addEventListener("scroll", onScroll, { passive: true });
    jumpButton.addEventListener("click", follow);
    render();
    return {
        update(isStreaming) {
            streaming = isStreaming;
            if (streaming && following) requestAnimationFrame(follow);
            render();
        },
        follow,
        dispose() {
            container.removeEventListener("scroll", onScroll);
            jumpButton.removeEventListener("click", follow);
        }
    };
}

export async function writeText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

export function addCodeCopyButtons() {
    for (const block of document.querySelectorAll(".markdown-body pre")) {
        if (block.querySelector(":scope > .code-copy-btn")) continue;
        const button = document.createElement("button");
        button.type = "button";
        button.className = "code-copy-btn";
        button.textContent = "Copy";
        button.addEventListener("click", async () => {
            const code = block.querySelector("code")?.textContent ?? block.textContent ?? "";
            if (await writeText(code)) {
                button.textContent = "Copied";
                setTimeout(() => { button.textContent = "Copy"; }, 1200);
            }
        });
        block.append(button);
    }
}

export function shouldSubmitOnEnter(event, hasFinePointer) {
    if (event.key !== "Enter" || event.isComposing) return false;
    return hasFinePointer ? !event.shiftKey : event.ctrlKey || event.metaKey;
}
