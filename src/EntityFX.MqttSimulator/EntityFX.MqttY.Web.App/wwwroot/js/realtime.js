// One SignalR connection; subscribers survive reconnect and wait until handshake completes.
let connection = null;
let started = null;
const subscriptions = new Map();

export function connect() {
    if (connection) return connection;
    connection = new signalR.HubConnectionBuilder().withUrl('/hubs/monitoring').withAutomaticReconnect().build();
    for (const [event, key] of [['MonitoringUpdate', 'onMonitoring'], ['NodeStatusUpdate', 'onNode'], ['SimulationStateChanged', 'onState']]) {
        connection.on(event, value => {
            for (const [id, handlers] of subscriptions) {
                if (value.simulationId && value.simulationId !== id || value.state && value.id && value.id !== id) continue;
                for (const handler of handlers) handler[key]?.(value);
            }
        });
    }
    connection.onreconnected(async () => {
        for (const id of subscriptions.keys()) await connection.invoke('SubscribeMonitoring', id);
    });
    started = connection.start();
    started.catch(() => { /* REST fallback remains available. */ });
    return connection;
}
export function subscribe(id, handlers) {
    const conn = connect();
    if (!subscriptions.has(id)) subscriptions.set(id, new Set());
    subscriptions.get(id).add(handlers);
    started.then(() => conn.invoke('SubscribeMonitoring', id)).catch(() => {});
    return () => unsubscribe(id, handlers);
}
export function unsubscribe(id, handlers) {
    const set = subscriptions.get(id);
    if (handlers) set?.delete(handlers); else set?.clear();
    if (set?.size) return;
    subscriptions.delete(id);
    if (connection) started.then(() => connection.invoke('UnsubscribeMonitoring', id)).catch(() => {});
}
