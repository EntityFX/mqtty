// Простой hash-роутер
const routes = new Map();

export function register(path, render) {
    routes.set(path, render);
}

export function navigate(path) {
    location.hash = path;
}

export function start() {
    window.addEventListener('hashchange', render);
    render();
}

async function render() {
    const path = (location.hash || '#/').replace(/^#/, '');
    const app = document.getElementById('app');
    const handler = routes.get(path) || routes.get('/');
    window.dispatchEvent(new Event('mqtty:before-navigation'));
    const page = document.createElement('div');
    app.replaceChildren(page);
    if (handler) {
        try { await handler(page); } catch(error) { page.textContent='Ошибка открытия экрана: '+error.message; }
    } else {
        app.innerHTML = '<div class="alert alert-warning">Страница не найдена</div>';
    }
}
