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

function render() {
    const path = (location.hash || '#/').replace(/^#/, '');
    const app = document.getElementById('app');
    const handler = routes.get(path) || routes.get('/');
    app.innerHTML = '';
    if (handler) {
        handler(app);
    } else {
        app.innerHTML = '<div class="alert alert-warning">Страница не найдена</div>';
    }
}
