import { api } from './api.js';
import { renderGraph, renderLegend } from './graph-renderer.js';
import { applyCircular, applyGrid } from './layout.js';

export async function renderDesigner(app) {
    const saved = await api.get('/topologies');

    app.innerHTML = `
        <h4>Редактор графа</h4>
        <div class="row g-3">
            <div class="col-md-3">
                <div class="card p-3">
                    <h6>Добавить узел</h6>
                    <div class="d-grid gap-2">
                        <button class="btn btn-outline-primary btn-sm" data-add="Network">Сеть</button>
                        <button class="btn btn-outline-success btn-sm" data-add="Server">Брокер</button>
                        <button class="btn btn-outline-warning btn-sm" data-add="Client">Клиент</button>
                        <button class="btn btn-outline-secondary btn-sm" data-add="Application">Relay</button>
                    </div>
                    <hr />
                    <h6>Расстановка</h6>
                    <div class="d-grid gap-2">
                        <button id="dsg-layout-circular" class="btn btn-outline-dark btn-sm">Circular</button>
                        <button id="dsg-layout-grid" class="btn btn-outline-dark btn-sm">Grid</button>
                    </div>
                    <hr />
                    <h6>Сохранение</h6>
                    <div class="mb-2">
                        <label class="form-label">Имя</label>
                        <input id="dsg-name" class="form-control form-control-sm" value="Моя топология" />
                    </div>
                    <button id="dsg-save" class="btn btn-primary btn-sm w-100">Сохранить</button>
                    <div class="mt-2">
                        <label class="form-label">Загрузить</label>
                        <select id="dsg-load" class="form-select form-select-sm">
                            <option value="">— выбрать —</option>
                            ${saved.map((t) => `<option value="${t.id}">${t.name}</option>`).join('')}
                        </select>
                    </div>
                </div>
            </div>
            <div class="col-md-9">
                <div class="card p-3">
                    <div id="dsg-legend" class="mb-2"></div>
                    <svg id="dsg-canvas" class="border rounded"></svg>
                </div>
            </div>
        </div>`;

    renderLegend(document.getElementById('dsg-legend'));

    const graph = { networks: [], nodes: [], links: [] };
    const canvas = document.getElementById('dsg-canvas');
    let nextId = 0;

    const stored = sessionStorage.getItem('mqtty-editor-graph');
    if (stored) {
        const loaded = JSON.parse(stored);
        graph.networks = loaded.networks || [];
        graph.nodes = loaded.nodes || [];
        graph.links = loaded.links || [];
        sessionStorage.removeItem('mqtty-editor-graph');
    } else {
        applyCircular(graph);
    }

    const redraw = () => renderGraph(canvas, graph, (id) => {
        const node = graph.nodes.find((n) => n.id === id);
        if (node && node.type === 'Server') {
            window.dispatchEvent(new CustomEvent('mqtty:broker-click', { detail: { nodeId: id } }));
        }
    }, { draggable: true });

    const addNode = (type) => {
        const id = `${type.toLowerCase()}-${nextId++}`;
        const network = graph.networks[0] ? graph.networks[0].id : null;
        const spec = type === 'Server' ? 'mqtt-broker' : type === 'Client' ? 'mqtt-client' : 'mqtt-relay';
        graph.nodes.push({ id, name: id, type, specification: spec, network, x: 200 + Math.random() * 300, y: 200 + Math.random() * 200 });
        redraw();
    };

    const addNetwork = () => {
        const id = `net-${nextId++}`;
        graph.networks.push({ id, name: id, networkType: 'ethernet', x: 300 + Math.random() * 200, y: 200 + Math.random() * 150 });
        redraw();
    };

    app.querySelectorAll('[data-add]').forEach((btn) => {
        btn.addEventListener('click', () => {
            if (btn.dataset.add === 'Network') addNetwork();
            else addNode(btn.dataset.add);
        });
    });

    document.getElementById('dsg-layout-circular').addEventListener('click', () => { applyCircular(graph); redraw(); });
    document.getElementById('dsg-layout-grid').addEventListener('click', () => { applyGrid(graph); redraw(); });

    document.getElementById('dsg-save').addEventListener('click', async () => {
        const name = document.getElementById('dsg-name').value || 'Моя топология';
        await api.post('/topologies', { name, graph, spec: { engine: 'relay' } });
        alert('Топология сохранена');
    });

    document.getElementById('dsg-load').addEventListener('change', async (e) => {
        if (!e.target.value) return;
        const savedTopology = await api.get(`/topologies/${e.target.value}`);
        graph.networks = savedTopology.graph.networks || [];
        graph.nodes = savedTopology.graph.nodes || [];
        graph.links = savedTopology.graph.links || [];
        redraw();
    });

    redraw();
}
