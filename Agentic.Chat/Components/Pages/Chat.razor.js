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
