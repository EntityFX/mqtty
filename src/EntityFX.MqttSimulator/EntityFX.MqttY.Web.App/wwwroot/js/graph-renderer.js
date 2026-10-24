// Рендер графа в SVG с фиксированной цветовой схемой узлов
export const NODE_COLORS = {
    Network: '#1565C0',
    'Server:mqtt-broker': '#2E7D32',
    'Client:mqtt-client': '#E65100',
    'Client:mqtt-publisher': '#E65100',
    'Client:mqtt-subscriber': '#E65100',
    'Application:mqtt-relay': '#7B1FA2',
    'Application:mqtt-load-balancer': '#AD1457',
    'Application:mqtt-federation': '#F9A825',
    'Application:default': '#546E7A',
    Link: '#90A4AE',
    Connection: '#B0BEC5',
};

export const LEGEND = [
    { label: 'Сеть', color: NODE_COLORS.Network },
    { label: 'Брокер', color: NODE_COLORS['Server:mqtt-broker'] },
    { label: 'Клиент', color: NODE_COLORS['Client:mqtt-client'] },
    { label: 'Relay', color: NODE_COLORS['Application:mqtt-relay'] },
    { label: 'Load-balancer', color: NODE_COLORS['Application:mqtt-load-balancer'] },
    { label: 'Federation', color: NODE_COLORS['Application:mqtt-federation'] },
];

export function nodeColor(type, specification) {
    const key = `${type}:${specification || 'default'}`;
    return NODE_COLORS[key] || NODE_COLORS[`${type}:default`] || NODE_COLORS['Application:default'];
}

export function renderLegend(container) {
    container.innerHTML = LEGEND.map((item) =>
        `<span class="legend-item"><span class="legend-swatch" style="background:${item.color}"></span>${item.label}</span>`
    ).join('');
}

export function renderGraph(svg, graph, onNodeClick) {
    const width = svg.clientWidth || 900;
    const height = svg.clientHeight || 500;
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.innerHTML = '';

    const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
    defs.innerHTML = '<marker id="arrowhead" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="#90a4ae"/></marker>';
    svg.appendChild(defs);

    const positions = new Map();
    graph.networks.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: 'Network', spec: n.networkType }));
    graph.nodes.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: n.type, spec: n.specification }));

    // Связи между сетями
    graph.links.forEach((link) => {
        const from = positions.get(link.from);
        const to = positions.get(link.to);
        if (!from || !to) return;
        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('class', 'link');
        line.setAttribute('x1', from.x);
        line.setAttribute('y1', from.y);
        line.setAttribute('x2', to.x);
        line.setAttribute('y2', to.y);
        svg.appendChild(line);
    });

    // Подключения узлов к сетям
    graph.nodes.forEach((node) => {
        const net = positions.get(node.network);
        const pos = positions.get(node.id);
        if (!net || !pos || node.network === node.id) return;
        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('class', 'connection');
        line.setAttribute('x1', net.x);
        line.setAttribute('y1', net.y);
        line.setAttribute('x2', pos.x);
        line.setAttribute('y2', pos.y);
        svg.appendChild(line);
    });

    // Узлы
    positions.forEach((pos, id) => {
        const isNetwork = pos.type === 'Network';
        const color = isNetwork ? NODE_COLORS.Network : nodeColor(pos.type, pos.spec);
        const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        g.setAttribute('class', 'node');
        g.setAttribute('data-id', id);
        g.setAttribute('data-type', pos.type);
        g.setAttribute('data-spec', pos.spec || '');

        if (isNetwork) {
            const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
            rect.setAttribute('x', pos.x - 70);
            rect.setAttribute('y', pos.y - 25);
            rect.setAttribute('width', 140);
            rect.setAttribute('height', 50);
            rect.setAttribute('rx', 8);
            rect.setAttribute('fill', color);
            g.appendChild(rect);
        } else {
            const shape = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            shape.setAttribute('cx', pos.x);
            shape.setAttribute('cy', pos.y);
            shape.setAttribute('r', 18);
            shape.setAttribute('fill', color);
            g.appendChild(shape);
        }

        const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        text.setAttribute('x', pos.x);
        text.setAttribute('y', isNetwork ? pos.y + 4 : pos.y + 34);
        text.setAttribute('text-anchor', 'middle');
        text.textContent = id;
        g.appendChild(text);

        if (onNodeClick) {
            g.addEventListener('click', () => onNodeClick(id, pos));
        }

        svg.appendChild(g);
    });
}
