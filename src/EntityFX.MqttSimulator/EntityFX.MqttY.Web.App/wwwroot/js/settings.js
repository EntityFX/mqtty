import { api } from './api.js';

export async function renderSettings(app) {
    const settings = await api.get('/settings');

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
            <button id="set-save" class="btn btn-primary">Сохранить</button>
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
}
