// Алгоритмы расстановки узлов: circular и grid
export function applyCircular(graph) {
    const networks = graph.networks;
    const nodes = graph.nodes;
    const cx = 450;
    const cy = 250;
    const radius = Math.max(180, networks.length * 80);

    networks.forEach((n, i) => {
        const angle = (2 * Math.PI * i) / Math.max(1, networks.length);
        n.x = cx + radius * Math.cos(angle);
        n.y = cy + radius * Math.sin(angle);
    });

    nodes.forEach((n, i) => {
        const net = networks.find((x) => x.id === n.network);
        const baseX = net ? net.x : cx;
        const baseY = net ? net.y : cy;
        const angle = (2 * Math.PI * i) / Math.max(1, nodes.length);
        n.x = baseX + 120 * Math.cos(angle);
        n.y = baseY + 120 * Math.sin(angle);
    });
}

export function applyGrid(graph) {
    const networks = graph.networks;
    const nodes = graph.nodes;
    const cols = Math.max(1, Math.ceil(Math.sqrt(networks.length)));

    networks.forEach((n, i) => {
        n.x = 120 + (i % cols) * 220;
        n.y = 100 + Math.floor(i / cols) * 160;
    });

    nodes.forEach((n, i) => {
        const net = networks.find((x) => x.id === n.network);
        const baseX = net ? net.x : 120;
        const baseY = net ? net.y : 100;
        n.x = baseX + 40 + (i % 3) * 60;
        n.y = baseY + 60 + Math.floor(i / 3) * 50;
    });
}
