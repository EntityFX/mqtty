// Черновики принадлежат браузеру; научные результаты сохраняются сервером отдельно.
export const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export function readDraft(key) {
    try { return JSON.parse(sessionStorage.getItem(key) || 'null'); } catch { return null; }
}
export function saveDraft(key, value) {
    try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* Серверное сохранение остаётся доступным. */ }
}
export function showMessage(app, message, error = false) {
    const box = app.querySelector('[data-feedback]');
    if (!box) return;
    box.hidden = false;
    box.className = `alert py-2 ${error ? 'alert-danger' : 'alert-success'}`;
    box.textContent = message;
}
