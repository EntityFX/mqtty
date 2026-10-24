import { api } from './api.js';

export async function renderPresets(app) {
    app.innerHTML = `
        <h4>Пресеты</h4>
        <ul class="nav nav-tabs mb-3">
            <li class="nav-item"><a class="nav-link active" data-tab="brokers" href="#">Брокеры</a></li>
            <li class="nav-item"><a class="nav-link" data-tab="topologies" href="#">Топологии</a></li>
        </ul>
        <div id="presets-content"></div>`;

    const content = document.getElementById('presets-content');

    const renderBrokers = async () => {
        const brokers = await api.get('/presets/brokers');
        content.innerHTML = `
            <button id="pr-broker-add" class="btn btn-primary btn-sm mb-2">Добавить пресет брокера</button>
            <table class="table table-sm table-striped">
                <thead><tr><th>Имя</th><th>Тип</th><th>Профиль</th><th></th></tr></thead>
                <tbody>
                    ${brokers.map((b) => `
                        <tr>
                            <td>${b.name}</td>
                            <td>${b.brokerType}</td>
                            <td><code>${b.profilePath}</code></td>
                            <td><button class="btn btn-outline-danger btn-sm" data-del="${b.id}">Удалить</button></td>
                        </tr>`).join('')}
                </tbody>
            </table>`;

        document.getElementById('pr-broker-add').addEventListener('click', () => {
            const name = prompt('Имя пресета:');
            const type = prompt('Тип брокера (Mosquitto/Aedes/ActiveMQ/EMQX):');
            if (!name || !type) return;
            api.post('/presets/brokers', {
                name, brokerType: type, profilePath: `profiles/${type}/broker-calibration.v3.json`,
                defaults: { payloadBytes: 16, qos: 1, publishers: 64, offeredRps: 1000 },
            }).then(renderBrokers);
        });

        content.querySelectorAll('[data-del]').forEach((btn) => {
            btn.addEventListener('click', () => {
                api.del(`/presets/brokers/${btn.dataset.del}`).then(renderBrokers);
            });
        });
    };

    const renderTopologies = async () => {
        const topologies = await api.get('/presets/topologies');
        content.innerHTML = `
            <table class="table table-sm table-striped">
                <thead><tr><th>Имя</th><th>Вид</th><th>Движок</th><th></th></tr></thead>
                <tbody>
                    ${topologies.map((t) => `
                        <tr>
                            <td>${t.name}</td>
                            <td>${t.kind}</td>
                            <td>${t.engine}</td>
                            <td><button class="btn btn-outline-danger btn-sm" data-del="${t.id}">Удалить</button></td>
                        </tr>`).join('')}
                </tbody>
            </table>`;

        content.querySelectorAll('[data-del]').forEach((btn) => {
            btn.addEventListener('click', () => {
                api.del(`/presets/topologies/${btn.dataset.del}`).then(renderTopologies);
            });
        });
    };

    app.querySelectorAll('[data-tab]').forEach((tab) => {
        tab.addEventListener('click', (e) => {
            e.preventDefault();
            app.querySelectorAll('[data-tab]').forEach((t) => t.classList.remove('active'));
            tab.classList.add('active');
            if (tab.dataset.tab === 'brokers') renderBrokers();
            else renderTopologies();
        });
    });

    renderBrokers();
}
