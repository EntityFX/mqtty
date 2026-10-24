import { api } from './api.js';
import { renderGraph, renderLegend } from './graph-renderer.js';
import { subscribe, unsubscribe } from './realtime.js';
import { openBrokerDialog } from './broker-dialog.js';

let currentSimulationId = null;
let releaseMonitoring = null;

export async function renderMonitoring(app) {
    const simulations = await api.get('/simulations');

    app.innerHTML = `
        <h4>Мониторинг симуляций</h4>
        <div class="row g-3">
            <div class="col-md-5">
                <div class="card p-3">
                    <table class="table table-sm table-striped monitor-table">
                        <thead><tr><th>ID</th><th>Движок</th><th>Статус</th><th>Ticks</th><th></th></tr></thead>
                        <tbody>
                            ${simulations.map((s) => `
                                <tr>
                                    <td><code>${s.id.slice(0, 8)}</code></td>
                                    <td>${s.engine}</td>
                                    <td><span class="badge bg-${stateColor(s.state)} state-badge">${s.state}</span></td>
                                    <td>${s.totalTicks}</td>
                                    <td>
                                        <button class="btn btn-outline-primary btn-sm" data-select="${s.id}">Открыть</button>
                                        <button class="btn btn-outline-danger btn-sm" data-stop="${s.id}">Стоп</button>
                                    </td>
                                </tr>`).join('')}
                        </tbody>
                    </table>
                </div>
            </div>
            <div class="col-md-7">
                <div class="card p-3">
                    <div id="mon-legend" class="mb-2"></div>
                    <svg id="mon-canvas" class="border rounded"></svg>
                    <div class="mt-2">
                        <h6>Состояние процесса</h6><pre id="mon-process"></pre><h6>Снимок модели</h6>
                        <pre id="mon-log" class="border rounded p-2" style="max-height:180px;overflow:auto;font-size:12px"></pre>
                    </div>
                </div>
            </div>
        </div>`;

    renderLegend(document.getElementById('mon-legend'));

    app.querySelectorAll('[data-select]').forEach((btn) => {
        btn.addEventListener('click', () => selectSimulation(btn.dataset.select));
    });

    app.querySelectorAll('[data-stop]').forEach((btn) => {
        btn.addEventListener('click', async () => {
            await api.post(`/simulations/${btn.dataset.stop}/stop`);
            renderMonitoring(app);
        });
    });

    if (simulations.length > 0) {
        selectSimulation(simulations[0].id);
    }
}

function stateColor(state) {
    switch (state) {
        case 'Completed': return 'success';
        case 'Running': return 'primary';
        case 'Failed': case 'TimedOut': case 'Interrupted': return 'danger';
        case 'Cancelled': return 'secondary';
        case 'Paused': return 'warning';
        default: return 'secondary';
    }
}

function selectSimulation(id) {
    if (currentSimulationId) {
        releaseMonitoring?.();
    }
    currentSimulationId = id;

    releaseMonitoring = subscribe(id, {
        onMonitoring: (snapshot) => renderSnapshot(snapshot),
        onState: (status) => {
            const badge = document.querySelector(`[data-select="${status.id}"]`);
            if (badge) badge.closest('tr').querySelector('.state-badge').textContent = status.state;
        },
    });

    api.get(`/simulations/${id}/monitor`).then(renderSnapshot).catch(() => {});
}

function renderSnapshot(snapshot) {
    const canvas = document.getElementById('mon-canvas');
    if (!canvas) return;

    const graph = { networks: [], nodes: [], links: snapshot.links || [] };
    const byId = new Map();

    snapshot.nodes.forEach((node) => {
        if (node.type === 'Network') {
            graph.networks.push({ id: node.id, name: node.id, networkType: node.specification, x: 0, y: 0 });
        } else {
            graph.nodes.push({ id: node.id, name: node.id, type: node.type, specification: node.specification, network: node.network, x: 0, y: 0 });
        }
        byId.set(node.id, node);
    });

    // Простая расстановка: сети по кругу, узлы вокруг своей сети
    const cx = 450, cy = 250;
    graph.networks.forEach((n, i) => {
        const angle = (2 * Math.PI * i) / Math.max(1, graph.networks.length);
        n.x = cx + 200 * Math.cos(angle);
        n.y = cy + 200 * Math.sin(angle);
    });
    graph.nodes.forEach((n, i) => {
        const net = graph.networks.find((x) => x.id === n.network);
        const angle = (2 * Math.PI * i) / Math.max(1, graph.nodes.length);
        n.x = (net ? net.x : cx) + 110 * Math.cos(angle);
        n.y = (net ? net.y : cy) + 110 * Math.sin(angle);
    });

    renderGraph(canvas, graph, (nodeId) => {
        const node = byId.get(nodeId);
        if (node && node.type === 'Server') {
            openBrokerDialog(snapshot.simulationId, nodeId);
        }
    });

    const process = document.getElementById('mon-process');
    if (process && currentSimulationId) api.get(`/simulations/${currentSimulationId}`).then(s => {
        if (process.isConnected) process.textContent = `PID: ${s.processId ?? '—'} | RSS: ${(s.processWorkingSetBytes / 1024**2).toFixed(1)} MiB | CPU: ${s.processCpuSeconds?.toFixed(2) ?? '—'} s\n${s.phase ?? '—'} | ${s.state}\n${s.error ?? ''}`;
    }).catch(() => {});
    const log = document.getElementById('mon-log');
    if (log) {
        log.textContent = `Ticks: ${snapshot.totalTicks} | Step: ${snapshot.step} | Errors: ${snapshot.errors} | State: ${snapshot.state}`;
    }
}
