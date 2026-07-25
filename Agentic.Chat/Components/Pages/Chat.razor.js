async function copyText(text) {
    try {
        if (!navigator.clipboard?.writeText) {
            throw new Error("Clipboard API is unavailable.");
        }

        await navigator.clipboard.writeText(text);
        return true;
    } catch (error) {
        console.warn("Could not copy to the clipboard.", error);
        return false;
    }
}

function announceCopied() {
    const status = document.getElementById("chat-status");
    if (status) {
        status.textContent = "Copied";
    }
}

export async function writeText(text) {
    return copyText(text);
}

export function addCodeCopyButtons() {
    for (const pre of document.querySelectorAll(".markdown-body[data-copyable='true'] pre")) {
        if (pre.dataset.copyCodeReady) {
            continue;
        }

        const code = pre.querySelector("code");
        if (!code) {
            continue;
        }

        pre.dataset.copyCodeReady = "true";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "code-copy-btn";
        button.setAttribute("aria-label", "Copy code");
        button.textContent = "Copy";
        button.addEventListener("click", async () => {
            if (!await copyText(code.textContent ?? "")) {
                return;
            }

            button.classList.add("is-copied");
            button.setAttribute("aria-label", "Code copied");
            button.textContent = "Copied";
            announceCopied();

            window.clearTimeout(button.copyResetTimer);
            button.copyResetTimer = window.setTimeout(() => {
                button.classList.remove("is-copied");
                button.setAttribute("aria-label", "Copy code");
                button.textContent = "Copy";
            }, 1500);
        });
        pre.append(button);
    }
}

const maxRows = 6;

export function shouldSubmitOnEnter(event, hasFinePointer) {
    return event.key === "Enter" &&
        !event.shiftKey &&
        !event.isComposing &&
        hasFinePointer;
}

function resizeTextarea(textarea) {
    if (CSS.supports("field-sizing", "content")) {
        return;
    }

    const styles = getComputedStyle(textarea);
    const lineHeight = Number.parseFloat(styles.lineHeight);
    const verticalPadding =
        Number.parseFloat(styles.paddingBlockStart) +
        Number.parseFloat(styles.paddingBlockEnd);
    const maxHeight = (lineHeight * maxRows) + verticalPadding;

    textarea.style.height = "auto";
    textarea.style.height = `${Math.min(textarea.scrollHeight, maxHeight)}px`;
    textarea.style.overflowY = textarea.scrollHeight > maxHeight ? "auto" : "hidden";
}

document.addEventListener("input", (event) => {
    if (event.target instanceof HTMLTextAreaElement && event.target.id === "chat-input") {
        resizeTextarea(event.target);
    }
});

document.addEventListener("keydown", (event) => {
    if (!(event.target instanceof HTMLTextAreaElement) || event.target.id !== "chat-input") {
        return;
    }

    if (!shouldSubmitOnEnter(event, matchMedia("(pointer: fine)").matches)) {
        return;
    }

    event.preventDefault();
    event.target.form?.requestSubmit();
});

document.querySelectorAll("#chat-input").forEach(resizeTextarea);
