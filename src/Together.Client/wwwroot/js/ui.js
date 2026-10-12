window.tripUi = {
    dirty: false,
    opener: null,
    fit() {
        const dialog = document.getElementById('editor');
        if (!dialog) return;
        const zoom = Number.parseFloat(getComputedStyle(document.documentElement).zoom) || 1;
        dialog.style.maxWidth = `${window.innerWidth / zoom - 24}px`;
        dialog.style.maxHeight = `${window.innerHeight / zoom - 24}px`;
    },
    open(id) { this.opener = document.activeElement; this.fit(); document.getElementById(id).showModal(); },
    close(id) {
        document.getElementById(id)?.close(); this.dirty = false;
        if (this.opener?.isConnected) this.opener.focus(); else document.querySelector('h1')?.focus();
    },
    setDirty(value) { this.dirty = value; },
    focusError() { document.querySelector('dialog [aria-invalid="true"]')?.focus(); },
    focusField(id) {
        requestAnimationFrame(() => {
            const field = document.getElementById(id);
            if (field && document.querySelector('dialog[open]')?.contains(field)) {
                field.focus();
                field.scrollIntoView({ block: 'center' });
            }
        });
    }
};
window.addEventListener('resize', () => window.tripUi.fit());
document.addEventListener('keydown', event => {
    const dialog = document.querySelector('dialog[open]');
    if (!dialog || event.key !== 'Tab') return;
    const controls = [...dialog.querySelectorAll('button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),a[href],[tabindex="0"]')]
        .filter(element => element.getClientRects().length > 0);
    const first = controls[0], last = controls.at(-1);
    if (!first) return;
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
});
new MutationObserver(() => window.tripUi.fit()).observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
window.addEventListener('beforeunload', event => {
    if (window.tripUi.dirty) { event.preventDefault(); event.returnValue = ''; }
});
