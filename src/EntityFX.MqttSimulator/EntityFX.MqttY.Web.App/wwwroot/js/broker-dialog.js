import { api } from './api.js';
import { subscribe, unsubscribe } from './realtime.js';

let activeSimulationId = null;
let releaseNodeMonitoring = null;
let activeNodeId = null;

export function openBrokerDialog(simulationId, nodeId) {
    activeSimulationId = simulationId;
    activeNodeId = nodeId;
    const modal = new bootstrap.Modal(document.getElementById('brokerModal'));
    document.getElementById('brokerModalTitle').textContent = `Статус брокера: ${nodeId}`;
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
    api.get(`/simulations/${activeSimulationId}/nodes/${activeNodeId}`)
        .then(render)
        .catch(() => { /* хаб уже обновил */ });
}

function render(node) {
    const body = document.getElementById('brokerModalBody');
    if (!body) return;
    const counters = Object.entries(node.counters || {})
        .map(([k, v]) => `<tr><td>${k}</td><td>${v}</td></tr>`)
        .join('');

    body.innerHTML = `
        <table class="table table-sm">
            <tr><th>Имя</th><td>${node.name}</td></tr>
            <tr><th>Тип</th><td>${node.type} (${node.specification})</td></tr>
            <tr><th>Сеть</th><td>${node.network || '—'}</td></tr>
            <tr><th>Подключён</th><td>${node.connected ? 'да' : 'нет'}</td></tr>
            <tr><th>Очередь</th><td>${node.queueLength}</td></tr>
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
