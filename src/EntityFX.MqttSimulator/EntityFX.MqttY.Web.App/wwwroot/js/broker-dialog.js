import { api } from './api.js';
import { subscribe } from './realtime.js';
import { escapeHtml as esc } from './workspace-state.js';

let activeSimulationId = null;
let releaseNodeMonitoring = null;
let activeNodeId = null;

export function openBrokerDialog(simulationId, nodeId) {
    activeSimulationId = simulationId;
    activeNodeId = nodeId;
    const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('brokerModal'));
    document.getElementById('brokerModalTitle').textContent = `Статистика узла: ${nodeId}`;
    document.getElementById('brokerModalBody').innerHTML = '<div class="text-muted">Загрузка…</div>';
    modal.show();

    releaseNodeMonitoring?.();
    releaseNodeMonitoring = subscribe(simulationId, {
        onNode: (node) => {
            if (node.id === nodeId) {
                render(node);
            }
        },
    });

    refresh();
}

function refresh() {
    if (!activeSimulationId || !activeNodeId) return;
    api.get(`/simulations/${activeSimulationId}/nodes/${encodeURIComponent(activeNodeId)}`)
        .then(render)
        .catch(error => { document.getElementById('brokerModalBody').textContent='Статистика недоступна: '+error.message; });
}

function render(node) {
    const body = document.getElementById('brokerModalBody');
    if (!body) return;
    const counters = Object.entries(node.counters || {})
        .map(([k, v]) => `<tr><td>${esc(k)}</td><td>${esc(v)}</td></tr>`)
        .join('');

    body.innerHTML = `
        <table class="table table-sm">
            <tr><th>Имя</th><td>${esc(node.name)}</td></tr>
            <tr><th>Тип</th><td>${esc(node.type)} (${esc(node.specification)})</td></tr>
            <tr><th>Сеть</th><td>${esc(node.network || '—')}</td></tr>
            <tr><th>Подключён</th><td>${node.connected ? 'да' : 'нет'}</td></tr>
            <tr><th>Очередь</th><td>${node.queueLength ?? '—'}</td></tr>
        </table>
        <h6>Счётчики</h6>
        <table class="table table-sm table-striped">
            <thead><tr><th>Метрика</th><th>Значение</th></tr></thead>
            <tbody>${counters || '<tr><td colspan="2">нет данных</td></tr>'}</tbody>
        </table>`;
}

export function closeBrokerDialog() {
    if (activeSimulationId) {
        releaseNodeMonitoring?.();
        releaseNodeMonitoring = null;
    }
    activeSimulationId = null;
    activeNodeId = null;
}
