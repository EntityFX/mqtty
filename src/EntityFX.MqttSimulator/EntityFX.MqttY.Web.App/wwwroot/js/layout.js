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


// Детерминированная раскладка по связности, без изменения самого графа.
export function applySmart(graph) {
    const adjacency=new Map(graph.networks.map(n=>[n.id,new Set()]));
    for(const edge of graph.links||[]) { adjacency.get(edge.from)?.add(edge.to);adjacency.get(edge.to)?.add(edge.from); }
    const remaining=new Set(adjacency.keys());let offset=0;
    while(remaining.size) {
        const candidates=[...remaining];
        const endpoint=candidates.find(id=>adjacency.get(id).size<=1);
        const root=endpoint||candidates.sort((a,b)=>adjacency.get(b).size-adjacency.get(a).size)[0];
        const queue=[[root,0]],levels=new Map();remaining.delete(root);
        for(let cursor=0;cursor<queue.length;cursor++) {
            const [id,depth]=queue[cursor];if(!levels.has(depth))levels.set(depth,[]);levels.get(depth).push(id);
            for(const neighbor of adjacency.get(id)||[])if(remaining.delete(neighbor))queue.push([neighbor,depth+1]);
        }
        let maxY=0;
        for(const [depth,ids] of levels)ids.forEach((id,row)=>{
            const net=graph.networks.find(n=>n.id===id);net.x=180+depth*390;net.y=offset+150+row*400;maxY=Math.max(maxY,net.y);
            const members=graph.nodes.filter(n=>n.network===id);
            members.forEach((n,i)=>{const angle=2*Math.PI*i/Math.max(1,members.length);const radius=Math.max(110,Math.ceil(members.length/6)*75);n.x=net.x+radius*Math.cos(angle);n.y=net.y+radius*Math.sin(angle);});
        });
        offset=maxY+420;
    }
    graph.nodes.filter(n=>!adjacency.has(n.network)).forEach((n,i)=>{n.x=180+(i%4)*130;n.y=offset+100+Math.floor(i/4)*90;});
}
