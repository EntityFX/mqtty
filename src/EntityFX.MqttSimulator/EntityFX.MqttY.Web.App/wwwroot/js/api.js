// REST-клиент MqttY.Web
const API = '/api';

async function request(method, path, body) {
    const options = { method, headers: { 'Content-Type': 'application/json' } };
    if (body !== undefined) {
        options.body = JSON.stringify(body);
    }
    const response = await fetch(API + path, options);
    if (!response.ok) {
        let detail = response.statusText;
        try {
            const data = await response.json();
            detail = data.error || data.title || detail;
        } catch { /* ignore */ }
        throw new Error(detail);
    }
    if (response.status === 204) {
        return null;
    }
    return response.json();
}

export const api = {
    get: (path) => request('GET', path),
    post: (path, body) => request('POST', path, body),
    put: (path, body) => request('PUT', path, body),
    del: (path) => request('DELETE', path),
};
