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

export function renderGraph(svg, graph, onNodeClick, { draggable = false } = {}) {
    const width = svg.clientWidth || 900;
    const height = svg.clientHeight || 500;
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.innerHTML = '';

    const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
    defs.innerHTML = '<marker id="arrowhead" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="#90a4ae"/></marker>';
    svg.appendChild(defs);

    const positions = new Map();
    graph.networks.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: 'Network', spec: n.networkType, model: n }));
    graph.nodes.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: n.type, spec: n.specification, model: n }));

    // Fit the complete graph, including network boxes and labels, to this viewport.
    // Logical coordinates stay independent of CSS size and browser zoom.
    if (positions.size) {
        const points = [...positions.values()];
        const left = Math.min(...points.map((p) => p.x)) - 100;
        const top = Math.min(...points.map((p) => p.y)) - 70;
        const graphWidth = Math.max(200, Math.max(...points.map((p) => p.x)) + 100 - left);
        const graphHeight = Math.max(140, Math.max(...points.map((p) => p.y)) + 70 - top);
        svg.setAttribute('viewBox', `${left} ${top} ${graphWidth} ${graphHeight}`);
    }

    const edges = [];
    function updateEdge(edge) {
        const from = positions.get(edge.from);
        const to = positions.get(edge.to);
        edge.line.setAttribute('x1', from.x);
        edge.line.setAttribute('y1', from.y);
        edge.line.setAttribute('x2', to.x);
        edge.line.setAttribute('y2', to.y);
    }

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
        edges.push({ line, from: link.from, to: link.to });
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
        edges.push({ line, from: node.network, to: node.id });
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

        let moved = false;
        if (draggable) {
            g.classList.add('draggable');
            let drag = null;
            const initialX = pos.x;
            const initialY = pos.y;
            function point(event) {
                const matrix = svg.getScreenCTM();
                return matrix ? new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse()) : null;
            }
            g.addEventListener('pointerdown', (event) => {
                if (event.button !== 0 || !event.isPrimary) return;
                const start = point(event);
                if (!start) return;
                moved = false;
                drag = { pointer: event.pointerId, start, x: pos.x, y: pos.y, screenX: event.clientX, screenY: event.clientY };
                g.setPointerCapture(event.pointerId);
                event.preventDefault();
            });
            g.addEventListener('pointermove', (event) => {
                if (!drag || event.pointerId !== drag.pointer) return;
                if (!moved && Math.hypot(event.clientX - drag.screenX, event.clientY - drag.screenY) < 4) return;
                const current = point(event);
                if (!current) return;
                moved = true;
                g.classList.add('dragging');
                const bounds = svg.viewBox.baseVal;
                const marginX = pos.type === 'Network' ? 75 : 25;
                pos.x = Math.max(bounds.x + marginX, Math.min(bounds.x + bounds.width - marginX, drag.x + current.x - drag.start.x));
                pos.y = Math.max(bounds.y + 30, Math.min(bounds.y + bounds.height - 45, drag.y + current.y - drag.start.y));
                pos.model.x = pos.x;
                pos.model.y = pos.y;
                g.setAttribute('transform', `translate(${pos.x - initialX} ${pos.y - initialY})`);
                edges.filter((edge) => edge.from === id || edge.to === id).forEach(updateEdge);
            });
            function finish(event) {
                if (!drag || event.pointerId !== drag.pointer) return;
                drag = null;
                g.classList.remove('dragging');
                if (g.hasPointerCapture(event.pointerId)) g.releasePointerCapture(event.pointerId);
            }
            g.addEventListener('pointerup', finish);
            g.addEventListener('pointercancel', finish);
            g.addEventListener('lostpointercapture', finish);
        }
        if (onNodeClick) {
            g.addEventListener('click', () => { if (!moved) onNodeClick(id, pos); });
        }

        svg.appendChild(g);
    });
}
