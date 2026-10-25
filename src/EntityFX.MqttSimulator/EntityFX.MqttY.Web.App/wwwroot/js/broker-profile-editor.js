import { api } from './api.js';
import { jsonParameterForm, showJson } from './json-viewer.js';

export async function editBrokerProfile(id, onSaved) {
    const original = await api.get(`/presets/brokers/${encodeURIComponent(id)}/profile`);
    const dialog = document.createElement('dialog');
    dialog.className = 'profile-dialog';
    dialog.innerHTML = '<form><h5>Параметры профиля брокера</h5><p>Сохранение создаёт новый файл. Профили уже запущенных опытов остаются прежними. Новые числа требуют измерений: редактирование не подтверждает точность модели.</p><div data-parameters></div><p data-error class="text-danger" role="alert"></p><div class="d-flex gap-2"><button class="btn btn-primary" type="submit">Сохранить новую версию</button><button class="btn btn-outline-primary" type="button" data-json>Просмотр JSON</button><button class="btn btn-outline-secondary" type="button" data-close>Закрыть</button></div></form>';
    const read = jsonParameterForm(dialog.querySelector('[data-parameters]'), original);
    dialog.querySelector('[data-close]').onclick = () => dialog.close();
    dialog.querySelector('[data-json]').onclick = () => {
        try { showJson(read(), 'Профиль брокера'); }
        catch (error) { dialog.querySelector('[data-error]').textContent = error.message; }
    };
    dialog.querySelector('form').onsubmit = async event => {
        event.preventDefault();
        const button = dialog.querySelector('[type=submit]'); button.disabled = true;
        try {
            const saved = await api.put(`/presets/brokers/${encodeURIComponent(id)}/profile`, read());
            await onSaved(saved); dialog.close();
        } catch (error) { dialog.querySelector('[data-error]').textContent = error.message; }
        finally { button.disabled = false; }
    };
    const cleanup = () => { dialog.close(); dialog.remove(); };
    window.addEventListener('mqtty:before-navigation', cleanup, { once: true });
    dialog.addEventListener('close', () => {
        window.removeEventListener('mqtty:before-navigation', cleanup); dialog.remove();
    }, { once: true });
    document.body.append(dialog); dialog.showModal();
}
