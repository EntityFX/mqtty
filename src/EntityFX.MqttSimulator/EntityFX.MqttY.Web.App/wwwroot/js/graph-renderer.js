// Рендер графа в SVG с фиксированной цветовой схемой узлов, зумом, панорамой и соединениями
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
    if (type === 'Network') return NODE_COLORS.Network;
    const key = `${type}:${specification || 'default'}`;
    return NODE_COLORS[key] || NODE_COLORS[`${type}:default`] || NODE_COLORS['Application:default'];
}

export function renderLegend(container) {
    container.innerHTML = LEGEND.map((item) =>
        `<span class="legend-item"><span class="legend-swatch" style="background:${item.color}"></span>${item.label}</span>`
    ).join('');
}

/** Состояние камеры (зум + панорама) для одного SVG-холста. */
const cameraState = new WeakMap();

function getCamera(svg, width, height) {
    let cam = cameraState.get(svg);
    if (!cam) {
        cam = { x: 0, y: 0, w: width, h: height };
        cameraState.set(svg, cam);
    }
    return cam;
}

function applyCamera(svg, cam) {
    svg.setAttribute('viewBox', `${cam.x} ${cam.y} ${cam.w} ${cam.h}`);
}

export function renderGraph(svg, graph, onNodeClick, { draggable = false, connecting = false, onConnectStart = null, onConnectEnd = null, selectedId = null, onPositionChange = null } = {}) {
    svg._listeners?.abort();
    const listeners = new AbortController();
    svg._listeners = listeners;
    const width = svg.clientWidth || 900;
    const height = svg.clientHeight || 500;

    const cam = getCamera(svg, width, height);
    applyCamera(svg, cam);
    svg.innerHTML = '';

    const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
    defs.innerHTML = '<marker id="arrowhead" markerWidth="8" markerHeight="6" refX="8" refY="3" orient="auto"><polygon points="0 0, 8 3, 0 6" fill="#90a4ae"/></marker>'
      + '<pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M 40 0 L 0 0 0 40" fill="none" stroke="#e0e0e0" stroke-width="0.5"/></pattern>';
    svg.appendChild(defs);
    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    bg.setAttribute('x', -100000); bg.setAttribute('y', -100000);
    bg.setAttribute('width', 200000); bg.setAttribute('height', 200000);
    bg.setAttribute('fill', 'url(#grid)');
    svg.appendChild(bg);

    const positions = new Map();
    graph.networks.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: 'Network', spec: n.networkType, model: n }));
    graph.nodes.forEach((n) => positions.set(n.id, { x: n.x, y: n.y, type: n.type, spec: n.specification, model: n }));

    if (positions.size && !svg._fitted) {
        const points = [...positions.values()];
        const left = Math.min(...points.map((p) => p.x)) - 100;
        const top = Math.min(...points.map((p) => p.y)) - 70;
        const graphWidth = Math.max(200, Math.max(...points.map((p) => p.x)) + 100 - left);
        const graphHeight = Math.max(140, Math.max(...points.map((p) => p.y)) + 70 - top);
        cam.x = left;
        cam.y = top;
        cam.w = graphWidth;
        cam.h = graphHeight;
        svg._fitted = true;
    }
    applyCamera(svg, cam);

    const edges = [];
    function updateEdge(edge) {
        const from = positions.get(edge.from);
        const to = positions.get(edge.to);
        if (!from || !to) return;
        edge.line.setAttribute('x1', from.x);
        edge.line.setAttribute('y1', from.y);
        edge.line.setAttribute('x2', to.x);
        edge.line.setAttribute('y2', to.y);
    }

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

    graph.nodes.forEach((node) => {
        const net = positions.get(node.network);
        const pos = positions.get(node.id);
        if (!net || !pos || node.network === node.id) return;
        const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        line.setAttribute('class', 'connection');
        line.setAttribute('x1', pos.x);
        line.setAttribute('y1', pos.y);
        line.setAttribute('x2', net.x);
        line.setAttribute('y2', net.y);
        edges.push({ line, from: node.id, to: node.network });
        svg.appendChild(line);
    });

    let tempLine = null;
    if (connecting && onConnectStart && onConnectEnd) {
        tempLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        tempLine.setAttribute('class', 'connection');
        tempLine.setAttribute('stroke', '#ff9800');
        tempLine.setAttribute('stroke-width', '2');
        tempLine.setAttribute('stroke-dasharray', '6 4');
        tempLine.setAttribute('visibility', 'hidden');
        svg.appendChild(tempLine);
    }

    function screenToGraph(x, y) {
        const pt = svg.createSVGPoint();
        pt.x = x;
        pt.y = y;
        const ctm = svg.getScreenCTM().inverse();
        return pt.matrixTransform(ctm);
    }

    svg.addEventListener('wheel', (event) => {
        event.preventDefault();
        const factor = event.deltaY > 0 ? 1.15 : 0.85;
        const rect = svg.getBoundingClientRect();
        const mx = event.clientX - rect.left;
        const my = event.clientY - rect.top;
        const svgX = cam.x + (mx / rect.width) * cam.w;
        const svgY = cam.y + (my / rect.height) * cam.h;
        const newW = cam.w * factor;
        const newH = cam.h * factor;
        cam.x = svgX - (mx / rect.width) * newW;
        cam.y = svgY - (my / rect.height) * newH;
        cam.w = newW;
        cam.h = newH;
        applyCamera(svg, cam);
    }, { passive: false, signal: listeners.signal });

    let panDrag = null;
    svg.addEventListener('pointerdown', (event) => {
        if (event.button !== 2 || !event.isPrimary) return;
        panDrag = { pointer: event.pointerId, startX: event.clientX, startY: event.clientY, camX: cam.x, camY: cam.y };
        svg.setPointerCapture(event.pointerId);
        event.preventDefault();
    }, { signal: listeners.signal });
    svg.addEventListener('pointermove', (event) => {
        if (!panDrag || event.pointerId !== panDrag.pointer) return;
        const rect = svg.getBoundingClientRect();
        const dx = event.clientX - panDrag.startX;
        const dy = event.clientY - panDrag.startY;
        cam.x = panDrag.camX - dx * (cam.w / rect.width);
        cam.y = panDrag.camY - dy * (cam.h / rect.height);
        applyCamera(svg, cam);
    }, { signal: listeners.signal });
    svg.addEventListener('pointerup', (event) => {
        if (panDrag && event.pointerId === panDrag.pointer) {
            panDrag = null;
            if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
        }
    }, { signal: listeners.signal });
    svg.addEventListener('pointercancel', (event) => {
        if (panDrag && event.pointerId === panDrag.pointer) panDrag = null;
        if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
    }, { signal: listeners.signal });

    let connectSourceId = null;
    positions.forEach((pos, id) => {
        const isNetwork = pos.type === 'Network';
        const color = nodeColor(pos.type, pos.spec);

        const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        g.setAttribute('class', `node ${isNetwork ? 'network' : pos.type.toLowerCase()}`);
        g.setAttribute('transform', `translate(${pos.x},${pos.y})`);
        g.setAttribute('tabindex', '0');
        g.setAttribute('role', 'button');
        g.setAttribute('aria-label', pos.model.name || id);
        g.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onNodeClick?.(id, pos); } });
        g.style.cursor = pos.type === 'Network' ? 'grab' : 'pointer';

        if (isNetwork) {
            const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
            rect.setAttribute('x', -70);
            rect.setAttribute('y', -25);
            rect.setAttribute('width', 140);
            rect.setAttribute('height', 50);
            rect.setAttribute('rx', 8);
            rect.setAttribute('fill', color);
            g.appendChild(rect);
        } else {
            const shape = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
            shape.setAttribute('cx', 0);
            shape.setAttribute('cy', 0);
            shape.setAttribute('r', 18);
            shape.setAttribute('fill', color);
            g.appendChild(shape);
        }

        const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        text.setAttribute('x', 0);
        text.setAttribute('y', isNetwork ? 4 : 34);
        text.setAttribute('text-anchor', 'middle');
        text.textContent = pos.model.name || id;
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
                if (connecting && onConnectStart) {
                    const start = point(event);
                    if (!start) return;
                    if (connectSourceId == null) {
                        connectSourceId = id;
                        onConnectStart(id, pos, start);
                    } else if (connectSourceId !== id && onConnectEnd) {
                        onConnectEnd(id, pos, start);
                        connectSourceId = null;
                    } else {
                        connectSourceId = null;
                    }
                    return;
                }
                const start = point(event);
                if (!start) return;
                moved = false;
                drag = { pointer: event.pointerId, start, x: pos.x, y: pos.y, screenX: event.clientX, screenY: event.clientY };
                g.setPointerCapture(event.pointerId);
                event.preventDefault();
            });
            g.addEventListener('pointermove', (event) => {
                if (connecting && onConnectStart && tempLine) {
                    const current = point(event);
                    if (!current) return;
                    tempLine.setAttribute('x1', pos.x);
                    tempLine.setAttribute('y1', pos.y);
                    tempLine.setAttribute('x2', current.x);
                    tempLine.setAttribute('y2', current.y);
                    tempLine.setAttribute('visibility', 'visible');
                    return;
                }
                if (!drag || event.pointerId !== drag.pointer) return;
                if (!moved && Math.hypot(event.clientX - drag.screenX, event.clientY - drag.screenY) < 4) return;
                const current = point(event);
                if (!current) return;
                moved = true;
                g.classList.add('dragging');
                pos.x = drag.x + current.x - drag.start.x;
                pos.y = drag.y + current.y - drag.start.y;
                pos.model.x = pos.x;
                pos.model.y = pos.y;
                g.setAttribute('transform', `translate(${pos.x},${pos.y})`);
                edges.filter((edge) => edge.from === id || edge.to === id).forEach(updateEdge);
            });
            function finish(event) {
                if (connecting) return;
                if (!drag || event.pointerId !== drag.pointer) return;
                drag = null;
                if (moved) onPositionChange?.(id, pos.model);
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

        if (selectedId && id === selectedId) {
            const ring = document.createElementNS('http://www.w3.org/2000/svg', isNetwork ? 'rect' : 'circle');
            if (isNetwork) {
                ring.setAttribute('x', -74); ring.setAttribute('y', -29); ring.setAttribute('width', 148); ring.setAttribute('height', 58); ring.setAttribute('rx', 10);
            } else {
                ring.setAttribute('cx', 0); ring.setAttribute('cy', 0); ring.setAttribute('r', 24);
            }
            ring.setAttribute('fill', 'none'); ring.setAttribute('stroke', '#ff9800'); ring.setAttribute('stroke-width', 3);
            ring.setAttribute('pointer-events', 'none');
            g.insertBefore(ring, g.firstChild);
        }

        svg.appendChild(g);
    });
}

export function zoomCamera(svg, factor) {
    const cam = getCamera(svg, svg.clientWidth || 900, svg.clientHeight || 500);
    const cx = cam.x + cam.w / 2;
    const cy = cam.y + cam.h / 2;
    cam.w = Math.max(50, cam.w * factor);
    cam.h = Math.max(40, cam.h * factor);
    cam.x = cx - cam.w / 2;
    cam.y = cy - cam.h / 2;
    applyCamera(svg, cam);
}

export function resetCamera(svg) {
    delete svg._fitted;
    const width = svg.clientWidth || 900;
    const height = svg.clientHeight || 500;
    const cam = getCamera(svg, width, height);
    cam.x = 0;
    cam.y = 0;
    cam.w = width;
    cam.h = height;
    applyCamera(svg, cam);
}
