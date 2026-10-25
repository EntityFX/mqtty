import { register, start } from './router.js';
import { renderTopologies } from './topology-generator.js';
import { renderDesigner } from './designer.js';
import { renderMonitoring } from './monitoring.js';
import { renderPresets } from './presets.js';
import { renderSettings } from './settings.js';
import { closeBrokerDialog } from './broker-dialog.js';
import { renderJsonViewer } from './json-viewer.js';

register('/', async (app) => {
    app.innerHTML = `
        <div class="p-4 text-center">
            <h3>MqttY.Web</h3>
            <p class="text-muted">Симулятор распределённых топологий сетей с MQTT-брокерами.</p>
            <div class="d-flex justify-content-center gap-2">
                <a class="btn btn-primary" href="#/designer">Создать или открыть топологию</a>
                <a class="btn btn-outline-secondary" href="#/simulations">Мониторинг</a>
            </div>
        </div>`;
});

register('/topologies', renderTopologies);
register('/designer', renderDesigner);
register('/simulations', renderMonitoring);
register('/presets', renderPresets);
register('/settings', renderSettings);
register('/json', renderJsonViewer);

document.getElementById('brokerModal').addEventListener('hidden.bs.modal', closeBrokerDialog);

start();
