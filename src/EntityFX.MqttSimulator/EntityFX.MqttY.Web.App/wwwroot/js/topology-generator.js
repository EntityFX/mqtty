import { api } from './api.js';
import { renderGraph, renderLegend } from './graph-renderer.js';
import { applyCircular } from './layout.js';
import { navigate } from './router.js';

export async function renderTopologies(app) {
    const presets = await api.get('/presets/topologies');
    const brokers = await api.get('/presets/brokers');

    app.innerHTML = `
        <h4>Генератор топологий</h4><p class="text-muted">MqttY выполняет модель. MqttBenchmark собирает реальные измерения и профили вне этого приложения. Combined доступен для проектирования; его исполнение пока не поддерживается.</p>
        <div class="row g-3">
            <div class="col-md-4">
                <div class="card p-3">
                    <div class="mb-2">
                        <label class="form-label">Пресет топологии</label>
                        <select id="tpl-preset" class="form-select">
                            <option value="">— параметры вручную —</option>
                            ${presets.map((p) => `<option value="${p.id}">${p.name}</option>`).join('')}
                        </select>
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Вид</label>
                        <select id="tpl-kind" class="form-select">
                            <option value="line">Линейная</option>
                            <option value="ring">Кольцо</option>
                            <option value="star">Звезда</option>
                            <option value="combined">Комбинированная</option>
                        </select>
                    </div>
                    <div class="row">
                        <div class="col-6 mb-2">
                            <label class="form-label">Длина цепочки</label>
                            <input id="tpl-chain" type="number" class="form-control" value="3" min="2" />
                        </div>
                        <div class="col-6 mb-2">
                            <label class="form-label">Лучи звезды</label>
                            <input id="tpl-rays" type="number" class="form-control" value="3" min="1" />
                        </div>
                    </div>
                    <div class="row">
                        <div class="col-6 mb-2">
                            <label class="form-label">Длина кольца</label>
                            <input id="tpl-ring" type="number" class="form-control" value="4" min="3" />
                        </div>
                        <div class="col-6 mb-2">
                            <label class="form-label">Клиентов на брокера</label>
                            <input id="tpl-clients" type="number" class="form-control" value="2" min="0" />
                        </div>
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Пресет брокера</label>
                        <select id="tpl-broker" class="form-select">
                            ${brokers.sort((a,b) => (a.id === 'ideal-demo' ? -1 : b.id === 'ideal-demo' ? 1 : 0)).map((b) => `<option value="${b.id}">${b.name}</option>`).join('')}
                        </select>
                    </div>
                    <div class="mb-2">
                        <label class="form-label">Движок</label>
                        <select id="tpl-engine" class="form-select">
                            <option value="relay">MqttRelay</option>
                            <option value="federation">Federation</option>
                            <option value="loadBalancing">Load-Balancer</option>
                        </select>
                    </div>
                    <div class="row">
                        <div class="col-6 mb-2">
                            <label class="form-label">QoS</label>
                            <input id="tpl-qos" type="number" class="form-control" value="1" min="0" max="2" />
                        </div>
                        <div class="col-6 mb-2">
                            <label class="form-label">Seed</label>
                            <input id="tpl-seed" type="number" class="form-control" value="42" />
                        </div>
                    </div>
                    <button id="tpl-generate" class="btn btn-primary w-100">Сгенерировать</button>
                </div>
            </div>
            <div class="col-md-8">
                <div class="card p-3">
                    <div id="tpl-legend" class="mb-2"></div>
                    <svg id="tpl-canvas" class="border rounded"></svg>
                    <div class="mt-2 d-flex gap-2">
                        <button id="tpl-run" class="btn btn-success" disabled>Запустить симуляцию</button>
                        <button id="tpl-edit" class="btn btn-outline-secondary" disabled>Открыть в редакторе</button>
                    </div>
                </div>
            </div>
        </div>`;

    renderLegend(document.getElementById('tpl-legend'));

    let current = null;

    document.getElementById('tpl-preset').addEventListener('change', (e) => {
        const preset = presets.find((p) => p.id === e.target.value);
        if (!preset) return;
        document.getElementById('tpl-kind').value = preset.kind;
        document.getElementById('tpl-chain').value = preset.chainLength;
        document.getElementById('tpl-rays').value = preset.rays;
        document.getElementById('tpl-ring').value = preset.ringLength;
        document.getElementById('tpl-broker').value = preset.brokerPresetId;
        document.getElementById('tpl-engine').value = preset.engine;
    });

    document.getElementById('tpl-generate').addEventListener('click', async () => {
        const spec = {
            kind: document.getElementById('tpl-kind').value,
            chainLength: document.getElementById('tpl-kind').value === 'star' ? 1 : +document.getElementById('tpl-chain').value,
            rays: +document.getElementById('tpl-rays').value,
            ringLength: +document.getElementById('tpl-ring').value,
            brokerPresetId: document.getElementById('tpl-broker').value,
            clientsPerBroker: +document.getElementById('tpl-clients').value,
            engine: document.getElementById('tpl-engine').value,
            qos: +document.getElementById('tpl-qos').value,
            seed: +document.getElementById('tpl-seed').value,
        };
        try {
            const result = await api.post('/topologies/generate', spec);
            current = { graph: result.graph, runOptions: result.runOptions, spec };
            applyCircular(current.graph);
            renderGraph(document.getElementById('tpl-canvas'), current.graph);
            document.getElementById('tpl-run').disabled = false;
            document.getElementById('tpl-edit').disabled = false;
        } catch (err) {
            alert(err.message);
        }
    });

    document.getElementById('tpl-run').addEventListener('click', async () => {
        if (!current) return;
        const body = {
            spec: current.spec,
            engine: current.spec.engine,
            runSettings: { timeLimit: '00:05:00', parallel: false, executionMode: 'OutOfProcess' },
        };
        const sim = await api.post('/simulations', body);
        navigate('/simulations');
    });

    document.getElementById('tpl-edit').addEventListener('click', () => {
        if (!current) return;
        sessionStorage.setItem('mqtty-editor-graph', JSON.stringify(current.graph));
        navigate('/designer');
    });
}
