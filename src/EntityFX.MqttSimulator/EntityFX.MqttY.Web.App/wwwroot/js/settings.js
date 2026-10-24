import { api } from './api.js';

export async function renderSettings(app) {
    const settings = await api.get('/settings');
    const brokers = await api.get('/presets/brokers');

    app.innerHTML = `
        <h4>Настройки</h4>
        <div class="card p-3" style="max-width:520px">
            <div class="mb-3">
                <label class="form-label">Лимит времени (минуты)</label>
                <input id="set-time" type="number" class="form-control" value="${settings.timeLimitMinutes ?? 10}" min="1" />
            </div>
            <div class="form-check mb-3">
                <input id="set-parallel" class="form-check-input" type="checkbox" ${settings.parallel ? 'checked' : ''} />
                <label class="form-check-label" for="set-parallel">Режим параллельного исполнения</label>
            </div>
            <div class="mb-3">
                <label class="form-label">Режим исполнения по умолчанию</label>
                <select id="set-mode" class="form-select">
                    <option value="InProcess" ${settings.executionMode === 'InProcess' ? 'selected' : ''}>In-process</option>
                    <option value="OutOfProcess" ${settings.executionMode === 'OutOfProcess' ? 'selected' : ''}>Out-of-process</option>
                </select>
            </div>
            <div class="mb-3">
                <label class="form-label">Максимум параллельных заданий</label>
                <input id="set-max" type="number" class="form-control" value="${settings.maxParallelJobs ?? 4}" min="1" />
            </div>
            <button id="set-save" class="btn btn-primary">Сохранить настройки</button>
        </div>

        <hr />
        <h6>Профили брокеров</h6>
        <div class="card p-3" style="max-width:700px">
            <div class="mb-3">
                <label class="form-label">Пресет брокера</label>
                <select id="bp-broker" class="form-select">
                    ${brokers.map((b) => `<option value="${b.profilePath}">${b.name} (${b.profilePath})</option>`).join('')}
                </select>
            </div>
            <div class="mb-3">
                <label class="form-label">Содержимое профиля (JSON)</label>
                <textarea id="bp-content" class="form-control" rows="10" style="font-family: monospace; font-size: 12px;"></textarea>
            </div>
            <div class="d-grid gap-2">
                <button id="bp-load" class="btn btn-outline-secondary btn-sm">Загрузить профиль</button>
                <button id="bp-save" class="btn btn-primary btn-sm">Сохранить профиль</button>
            </div>
            <div id="bp-status" class="mt-2 small"></div>
        </div>`;

    document.getElementById('set-save').addEventListener('click', async () => {
        const body = {
            timeLimit: `00:${String(+document.getElementById('set-time').value).padStart(2, '0')}:00`,
            parallel: document.getElementById('set-parallel').checked,
            executionMode: document.getElementById('set-mode').value,
            maxParallelJobs: +document.getElementById('set-max').value,
        };
        await api.put('/settings', body);
        alert('Настройки сохранены');
    });

    const statusEl = document.getElementById('bp-status');
    const contentEl = document.getElementById('bp-content');

    document.getElementById('bp-load').addEventListener('click', async () => {
        const profilePath = document.getElementById('bp-broker').value;
        try {
            const data = await api.get(`/settings/broker-profile?profilePath=${encodeURIComponent(profilePath)}`);
            contentEl.value = data.content;
            statusEl.textContent = `Загружен: ${profilePath}`;
            statusEl.className = 'mt-2 small text-success';
        } catch (err) {
            statusEl.textContent = 'Ошибка загрузки: ' + err.message;
            statusEl.className = 'mt-2 small text-danger';
        }
    });

    document.getElementById('bp-save').addEventListener('click', async () => {
        const profilePath = document.getElementById('bp-broker').value;
        try {
            await api.put('/settings/broker-profile', {
                profilePath,
                content: contentEl.value,
            });
            statusEl.textContent = 'Профиль сохранён.';
            statusEl.className = 'mt-2 small text-success';
        } catch (err) {
            statusEl.textContent = 'Ошибка сохранения: ' + err.message;
            statusEl.className = 'mt-2 small text-danger';
        }
    });
}
