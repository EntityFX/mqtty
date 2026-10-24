import { api } from './api.js';
import { renderGraph, renderLegend, resetCamera, zoomCamera } from './graph-renderer.js';
import { applyCircular, applyGrid, applySmart } from './layout.js';
import { navigate } from './router.js';
import { configureWorkspace } from './workspace-panels.js';
import { escapeHtml as esc, readDraft, saveDraft, showMessage } from './workspace-state.js';

export async function renderDesigner(app) {
    const [saved, brokers, presets] = await Promise.all([api.get('/topologies'), api.get('/presets/brokers'), api.get('/presets/topologies')]);
    if (!app.isConnected) return;
    app.innerHTML = `
        <div class="workspace-heading"><h4>Топология: создание и редактирование</h4><span class="text-muted small">Черновик сохраняется при переходе между экранами</span></div>
        <div data-feedback role="status" hidden></div>
        <div class="workspace">
          <aside class="workspace-sidebar card p-3">
            <label for="dsg-name" class="form-label">Название</label><input id="dsg-name" class="form-control form-control-sm" value="Моя топология">
            <div class="d-flex gap-1 my-2"><button id="dsg-save" class="btn btn-outline-primary btn-sm">💾 Сохранить изменения</button><button id="dsg-clone" class="btn btn-outline-secondary btn-sm">⧉ Копия</button><button id="dsg-export" class="btn btn-outline-secondary btn-sm">⇩ JSON</button></div>
            <button id="dsg-start-sim" class="btn btn-primary btn-sm mb-2">▶ Запустить симуляцию</button>
            <details open><summary>Создать топологию</summary><label class="form-label w-100">Шаблон<select id="dsg-preset" class="form-select form-select-sm mb-2"><option value="">Собственные параметры</option>${presets.map(p=>`<option value="${esc(p.id)}">${esc(p.name)}</option>`).join('')}</select></label><div class="control-grid">
              <label>Форма<select id="dsg-kind" class="form-select form-select-sm"><option value="line">Линия</option><option value="ring">Кольцо</option><option value="star">Звезда</option></select></label>
              <label>Брокеров<input id="dsg-count" type="number" min="2" max="16" value="3" class="form-control form-control-sm"></label>
              <label class="wide">Профиль для всего запуска<select id="dsg-broker" class="form-select form-select-sm">${brokers.sort((a,b)=>(a.id==='ideal-demo'?-1:b.id==='ideal-demo'?1:0)).map(b=>`<option value="${esc(b.id)}">${esc(b.name)}</option>`).join('')}</select></label>
              <label class="wide">Способ объединения<select id="dsg-engine" class="form-select form-select-sm"><option value="relay">MqttRelay</option><option value="federation">Федерация</option><option value="loadBalancing">Балансировщик</option></select></label>
              <label>Отправителей / брокер<input id="dsg-clients" type="number" min="1" value="2" class="form-control form-control-sm"></label>
              <label>QoS<select id="dsg-qos" class="form-select form-select-sm"><option>0</option><option selected>1</option><option>2</option></select></label>
              <label>Сообщений/с, всего<input id="dsg-rps" type="number" min="1" value="1000" class="form-control form-control-sm"></label>
              <label>Нагрузка, модельных с<input id="dsg-duration" type="number" min="0.02" step="0.1" value="0.2" class="form-control form-control-sm"></label>
              <label class="wide">Стратегия балансировки<select id="dsg-balancing" class="form-select form-select-sm"><option value="RoundRobin">По очереди</option><option value="LeastConnections">Меньше соединений</option><option value="DirectBalanced">Прямое распределение</option><option value="Single">Один брокер</option></select></label><label>Payload, байт<input id="dsg-payload" type="number" min="16" value="16" class="form-control form-control-sm"></label>
              <label>Seed<input id="dsg-seed" type="number" value="42" class="form-control form-control-sm"></label>
            </div><button id="dsg-tpl-generate" class="btn btn-success btn-sm w-100 mt-2">✦ Добавить топологию</button></details>
            <details><summary>Время и модельная сеть</summary><div class="control-grid">
              <label>Прогрев, с<input id="dsg-warmup" type="number" min="0" step="0.01" value="0.01" class="form-control form-control-sm"></label>
              <label>Ожидание завершения, с<input id="dsg-drain" type="number" min="0.01" step="0.1" value="1" class="form-control form-control-sm"></label>
              <label>Передача, шагов<input id="dsg-transfer" type="number" min="1" value="1" class="form-control form-control-sm"></label>
              <label>Очередь, пакетов<input id="dsg-queue" type="number" min="1" value="10000" class="form-control form-control-sm"></label>
              <label class="wide">Пропускная способность, байт/с<input id="dsg-capacity" type="number" min="1" value="100000000" class="form-control form-control-sm"></label>
            </div><p class="small text-muted mt-2">Шаг модели: 0,1 мс. Длительности заданы в модельных секундах.</p></details>
            <details open><summary>Выбранный узел</summary><select id="dsg-node" aria-label="Выбрать узел" class="form-select form-select-sm mb-2"><option value="">Выберите на схеме или в списке</option></select><div id="dsg-props" class="small text-muted">Выберите узел.</div></details>
            <details><summary>Изменить структуру</summary><p class="small text-muted">Соедините сети двух топологий. MqttRelay и федерация исполняют физический граф с промежуточными сетями. На площадке допускается один брокер; профиль и параметры сети общие для запуска.</p>
              <div class="control-grid">${[['Network','Сеть'],['Server','Брокер'],['Client','Клиент'],['Application','Relay']].map(([t,n])=>`<button data-add="${t}" class="btn btn-outline-${t==='Network'?'primary':t==='Server'?'success':t==='Client'?'warning':'secondary'} btn-sm">+ ${n}</button>`).join('')}</div>
              <button id="dsg-connect-mode" class="btn btn-outline-info btn-sm w-100 mt-2">🔗 Соединить</button><label class="form-label small mt-2 w-100">Существующие связи<select id="dsg-link" class="form-select form-select-sm"></select></label><button id="dsg-delete-link" class="btn btn-outline-danger btn-sm w-100">✂ Удалить связь</button><button id="dsg-delete-node" class="btn btn-outline-danger btn-sm w-100 mt-1">✕ Удалить узел</button>
            </details>
            <details><summary>Файлы и сохранённые топологии</summary><button id="dsg-new" class="btn btn-outline-danger btn-sm mb-2">✕ Очистить черновик</button><select id="dsg-load" class="form-select form-select-sm"><option value="">Загрузить…</option>${saved.map(t=>`<option value="${esc(t.id)}">${esc(t.name)}</option>`).join('')}</select><label class="form-label small mt-2">Импорт JSON<input id="dsg-import" type="file" class="form-control form-control-sm" accept=".json"></label></details>
          </aside>
          <section class="card p-3 workspace-main"><div class="graph-toolbar"><div id="dsg-legend"></div><div class="d-flex gap-1"><button id="dsg-layout-smart" class="btn btn-light btn-sm">Авто</button><button id="dsg-layout-circular" class="btn btn-light btn-sm">По кругу</button><button id="dsg-layout-grid" class="btn btn-light btn-sm">Сетка</button><button id="dsg-zoom-in" class="btn btn-light btn-sm" aria-label="Приблизить">＋</button><button id="dsg-zoom-out" class="btn btn-light btn-sm" aria-label="Отдалить">−</button><button id="dsg-reset-camera" class="btn btn-light btn-sm">Вписать</button></div></div><svg id="dsg-canvas" aria-label="Топология: выберите или перетащите узел"></svg><div id="dsg-info" class="small text-muted mt-2"></div></section>
        </div>`;
    const showPanel=configureWorkspace(app);
    const el = id => app.querySelector('#'+id);
    const feedback = (s,error=false) => showMessage(app,s,error);
    let draft = readDraft('mqtty-editor-draft');
    const transfer = readDraft('mqtty-editor-transfer');
    if (transfer) { draft = transfer; sessionStorage.removeItem('mqtty-editor-transfer'); }
    let graph = draft?.graph || readDraft('mqtty-editor-graph') || { networks:[], nodes:[], links:[] };
    if (graph.nodes.length && [...graph.nodes,...graph.networks].every(n=>!n.x&&!n.y)) applyCircular(graph);
    let spec = draft?.spec || null;
    let savedId=draft?.id||null;
    let selectedId = null, connecting = false, source = null;
    if (draft?.name) el('dsg-name').value = draft.name;
    function persist() { saveDraft('mqtty-editor-draft', { graph, spec, id:savedId, name:el('dsg-name').value }); }
    function select(id) { if(!connecting)showPanel('node');selectedId = id; el('dsg-node').value=id || ''; renderProps(); }
    function renderProps() {
        const m=[...graph.nodes,...graph.networks].find(n=>n.id===selectedId);
        if(!m) { el('dsg-props').textContent='Выберите узел.'; return; }
        const net=graph.networks.includes(m);
        const types=['mqtt-broker','mqtt-client','mqtt-publisher','mqtt-subscriber','mqtt-relay','mqtt-load-balancer','mqtt-federation'];
        el('dsg-props').innerHTML=`<label class="form-label w-100">Имя<input id="prop-name" class="form-control form-control-sm" value="${esc(m.name||m.id)}"></label>
          ${net?'':`<label class="form-label w-100">Назначение<select id="prop-spec" class="form-select form-select-sm">${[...new Set([m.specification,...types])].filter(Boolean).map(t=>`<option ${t===m.specification?'selected':''}>${esc(t)}</option>`).join('')}</select></label><label class="form-label w-100">Сеть<select id="prop-net" class="form-select form-select-sm"><option value="">Не задана</option>${graph.networks.map(n=>`<option value="${esc(n.id)}" ${m.network===n.id?'selected':''}>${esc(n.name||n.id)}</option>`).join('')}</select></label>`}
          <div class="control-grid"><label>X<input id="prop-x" type="number" class="form-control form-control-sm" value="${Math.round(m.x||0)}"></label><label>Y<input id="prop-y" type="number" class="form-control form-control-sm" value="${Math.round(m.y||0)}"></label></div><button id="prop-apply" class="btn btn-outline-primary btn-sm mt-2">✓ Применить</button>`;
        el('prop-apply').onclick=()=>{m.name=el('prop-name').value;m.x=Number(el('prop-x').value);m.y=Number(el('prop-y').value);if(!net){m.specification=el('prop-spec').value;m.type=m.specification==='mqtt-broker'?'Server':['mqtt-client','mqtt-publisher','mqtt-subscriber'].includes(m.specification)?'Client':'Application';m.network=el('prop-net').value;}redraw();};
    }
    function redraw() {
        const nodes=[...graph.networks,...graph.nodes];
        el('dsg-node').innerHTML='<option value="">Выберите узел…</option>'+nodes.map(n=>`<option value="${esc(n.id)}">${esc(n.name||n.id)}</option>`).join('');
        el('dsg-node').value=selectedId||'';
        el('dsg-link').innerHTML=graph.links.map((l,i)=>`<option value="${i}">${esc(l.from)} ↔ ${esc(l.to)}</option>`).join('');
        el('dsg-connect-mode').classList.toggle('btn-info',connecting);
        el('dsg-connect-mode').textContent=connecting?'🔗 Отменить соединение':'🔗 Соединить';
        renderGraph(el('dsg-canvas'),graph,select,{draggable:true,connecting,selectedId,
          onPositionChange:()=>{persist();renderProps();},
          onConnectStart:id=>{source=id;},onConnectEnd:id=>{
            if(!source||source===id)return;
            const fromNetwork=graph.networks.find(n=>n.id===source);
            const toNetwork=graph.networks.find(n=>n.id===id);
            if(fromNetwork&&toNetwork) {
              if(graph.links.some(l=>(l.from===source&&l.to===id)||(l.from===id&&l.to===source))){feedback('Эти сети уже связаны.',true);return;}
              graph.links.push({from:source,to:id,weight:1});
            } else {
              const net=fromNetwork||toNetwork;
              const node=graph.nodes.find(n=>n.id===source||n.id===id);
              if(!net||!node){feedback('Соединяйте две сети либо узел и сеть.',true);return;}
              node.network=net.id;
            }
            connecting=false;source=null;redraw();feedback('Связь добавлена.');
          }});
        el('dsg-info').textContent=`Сетей: ${graph.networks.length} · Узлов: ${graph.nodes.length} · ${connecting?'Соединение: выберите первую сеть / узел, затем вторую сеть':'Перетаскивание — левой кнопкой, панорама — правой, масштаб — колесом'}`;
        persist();
    }
    function syncControls() {
        if(!spec)return;
        for(const [id,v] of Object.entries({'dsg-kind':spec.kind,'dsg-count':spec.brokers??(spec.kind==='ring'?spec.ringLength:spec.kind==='star'?spec.rays+1:spec.chainLength),'dsg-broker':spec.brokerPresetId,'dsg-engine':spec.engine,'dsg-clients':spec.clientsPerBroker,'dsg-qos':spec.qos,'dsg-rps':spec.offeredRps,'dsg-duration':spec.measurementTicks/10000,'dsg-payload':spec.payloadBytes,'dsg-seed':spec.seed,'dsg-balancing':spec.balancingMode,'dsg-warmup':spec.warmupTicks/10000,'dsg-drain':spec.drainTimeoutTicks/10000,'dsg-transfer':spec.networkType?.transferTicks,'dsg-capacity':spec.networkType?.capacityBytesPerSecond,'dsg-queue':spec.networkType?.queueCapacity}))if(v!=null)el(id).value=v;
    }
    function editPreset(tab,id) { saveDraft('mqtty-preset-selection',{tab,id});navigate('/presets'); }
    for(const [id,tab] of [['dsg-broker','brokers'],['dsg-preset','topologies']]) {
      const button=document.createElement('button');button.type='button';button.className='btn btn-link btn-sm p-0';button.textContent=tab==='brokers'?'✎ Изменить пресет брокера':'✎ Изменить выбранный шаблон';button.onclick=()=>editPreset(tab,el(id).value);el(id).after(button);
    }
    el('dsg-preset').onchange=e=>{const preset=presets.find(p=>p.id===e.target.value);if(!preset)return;const original=spec;spec=preset;syncControls();spec=original;feedback('Параметры шаблона выбраны. Нажмите «Добавить топологию», чтобы добавить топологию.');};
    el('dsg-new').onclick=()=>{if(!confirm('Очистить рабочий граф? Сохранённые топологии останутся на сервере.'))return;graph={networks:[],nodes:[],links:[]};spec=null;savedId=null;selectedId=null;redraw();renderProps();showPanel('create');};
    el('dsg-node').onchange=e=>select(e.target.value);
    el('dsg-name').oninput=persist;
    function readRunSettings() {
        const count=Number(el('dsg-count').value),kind=el('dsg-kind').value;
        return {kind,chainLength:kind==='star'?1:count,ringLength:count,rays:count-1,brokerPresetId:el('dsg-broker').value,engine:el('dsg-engine').value,balancingMode:el('dsg-balancing').value,clientsPerBroker:Number(el('dsg-clients').value),qos:Number(el('dsg-qos').value),offeredRps:Number(el('dsg-rps').value),payloadBytes:Number(el('dsg-payload').value),seed:Number(el('dsg-seed').value),warmupTicks:Math.round(Number(el('dsg-warmup').value)*10000),measurementTicks:Math.round(Number(el('dsg-duration').value)*10000),drainTimeoutTicks:Math.round(Number(el('dsg-drain').value)*10000),networkType:{...(spec?.networkType||{}),name:spec?.networkType?.name||'ethernet',transferTicks:Number(el('dsg-transfer').value),capacityBytesPerSecond:Number(el('dsg-capacity').value),queueCapacity:Number(el('dsg-queue').value)}};
    }
    el('dsg-tpl-generate').onclick=async()=>{
      try {
        const count=Number(el('dsg-count').value),kind=el('dsg-kind').value;
        const next=readRunSettings();
        const result=await api.post('/topologies/generate',next);
        const added=result.graph;applySmart(added);
        if(graph.nodes.length||graph.networks.length) {
          const suffix='_'+crypto.randomUUID().slice(0,8);
          const ids=new Map([...added.networks,...added.nodes].map(n=>[n.id,n.id+suffix]));
          const remap=value=>Array.isArray(value)?value.map(remap):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([k,v])=>[ids.get(k)||k,remap(v)])):typeof value==='string'?(ids.get(value)||value):value;
          const copy=remap(added);
          const right=Math.max(...[...graph.networks,...graph.nodes].map(n=>n.x||0));
          const left=Math.min(...[...copy.networks,...copy.nodes].map(n=>n.x||0));
          [...copy.networks,...copy.nodes].forEach(n=>{n.x+=right-left+250;});
          graph.networkTypes??={};
          for(const [name,value] of Object.entries(copy.networkTypes||{})) {
            const key=graph.networkTypes[name]&&JSON.stringify(graph.networkTypes[name])!==JSON.stringify(value)?name+suffix:name;
            graph.networkTypes[key]=value;copy.networks.forEach(n=>{if(n.networkType===name)n.networkType=key;});
          }
          graph.networks.push(...copy.networks);graph.nodes.push(...copy.nodes);graph.links.push(...copy.links);
          next.seed=graph.randomSeed??next.seed;
        } else graph=added;
        spec=next;selectedId=null;resetCamera(el('dsg-canvas'));redraw();renderProps();feedback('Топология добавлена. Если это отдельная часть, соедините её сеть с существующей. Параметры нагрузки и профиль применяются ко всему запуску.');
      }catch(e){feedback(e.message,true);}
    };
    el('dsg-save').onclick=async()=>{try{const result=await api.post('/topologies',{id:savedId||'',name:el('dsg-name').value,graph,spec:spec||{engine:'relay'}});savedId=result.id;persist();feedback('Топология сохранена на сервере.');const list=await api.get('/topologies');el('dsg-load').innerHTML='<option value="">Загрузить…</option>'+list.map(t=>`<option value="${esc(t.id)}">${esc(t.name)}</option>`).join('');}catch(e){feedback(e.message,true);}};
    el('dsg-clone').onclick=async()=>{savedId=null;el('dsg-name').value='Копия '+el('dsg-name').value;await el('dsg-save').onclick();persist();};
    el('dsg-start-sim').onclick=async()=>{
      const button=el('dsg-start-sim');button.disabled=true;
      try{if(!graph.nodes.length)throw new Error('Добавьте топологию или загрузите сохранённый граф.');
        spec=readRunSettings();graph.randomSeed=spec.seed;
        for(const value of Object.values(graph.networkTypes||{})){value.capacityBytesPerSecond=spec.networkType.capacityBytesPerSecond;value.queueCapacity=spec.networkType.queueCapacity;value.transferTicks=spec.networkType.transferTicks;}
        const saved=await api.post('/topologies',{id:savedId||'',name:el('dsg-name').value,graph,spec});savedId=saved.id;persist();
        const repeats=Number(el('dsg-repeats').value),qualities=el('dsg-all-qos').checked?[0,1,2]:[spec.qos];
        const requests=qualities.flatMap(qos=>Array.from({length:repeats},(_,i)=>{
          const seed=spec.seed+i;const input=structuredClone(graph);input.randomSeed=seed;
          return {topologyId:saved.id,graph:input,spec:{...spec,qos,seed},engine:spec.engine,runSettings:{timeLimit:'00:05:00',executionMode:'OutOfProcess'}};
        }));
        // Check the whole series before enqueueing its first point.
        for(const request of requests)await api.post('/simulations',{...request,validateOnly:true});
        const created=[];
        try{for(const request of requests)created.push(await api.post('/simulations',request));}
        catch(error){throw new Error(`Поставлено в очередь ${created.length} из ${requests.length}. ${error.message}`);}
        saveDraft('mqtty-selected-simulation',created[0].id);navigate('/simulations');
      }catch(e){feedback(e.message,true);}finally{button.disabled=false;}
    };
    el('dsg-load').onchange=async e=>{try{if(!e.target.value)return;const saved=await api.get('/topologies/'+encodeURIComponent(e.target.value));graph=saved.graph;spec=saved.spec;savedId=saved.id;el('dsg-name').value=saved.name;selectedId=null;syncControls();resetCamera(el('dsg-canvas'));redraw();renderProps();}catch(e){feedback(e.message,true);}};
    el('dsg-import').onchange=async e=>{try{const file=e.target.files[0];if(!file)return;const saved=JSON.parse(await file.text());if(!saved.graph?.nodes||!saved.graph?.networks)throw new Error('Ожидается экспорт топологии с полем graph.');graph=saved.graph;spec=saved.spec||null;savedId=null;el('dsg-name').value=saved.name||file.name;selectedId=null;syncControls();redraw();renderProps();feedback('Импортировано в черновик. Для записи на сервер нажмите «Сохранить».');}catch(err){feedback(err.message,true);}};
    el('dsg-export').onclick=()=>{const url=URL.createObjectURL(new Blob([JSON.stringify({name:el('dsg-name').value,graph,spec},null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='topology.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
    app.querySelectorAll('[data-add]').forEach(button=>button.onclick=()=>{const type=button.dataset.add;const id=crypto.randomUUID();if(type==='Network')graph.networks.push({id,name:'Сеть',networkType:'ethernet',x:300,y:300});else graph.nodes.push({id,name:type,type,specification:type==='Server'?'mqtt-broker':type==='Client'?'mqtt-client':'mqtt-relay',network:graph.networks[0]?.id,x:400,y:300});redraw();select(id);});
    el('dsg-delete-node').onclick=()=>{graph.nodes=graph.nodes.filter(n=>n.id!==selectedId);graph.networks=graph.networks.filter(n=>n.id!==selectedId);graph.links=graph.links.filter(l=>l.from!==selectedId&&l.to!==selectedId);graph.nodes.forEach(n=>{if(n.network===selectedId)n.network=null;});selectedId=null;redraw();renderProps();};
    el('dsg-delete-link').onclick=()=>{const index=Number(el('dsg-link').value);if(el('dsg-link').value!=='')graph.links.splice(index,1);redraw();};
    el('dsg-connect-mode').onclick=()=>{connecting=!connecting;source=null;redraw();};
    el('dsg-layout-smart').onclick=()=>{applySmart(graph);resetCamera(el('dsg-canvas'));redraw();};
    el('dsg-layout-circular').onclick=()=>{applyCircular(graph);resetCamera(el('dsg-canvas'));redraw();};
    el('dsg-layout-grid').onclick=()=>{applyGrid(graph);resetCamera(el('dsg-canvas'));redraw();};
    el('dsg-zoom-in').onclick=()=>zoomCamera(el('dsg-canvas'),0.8);
    el('dsg-zoom-out').onclick=()=>zoomCamera(el('dsg-canvas'),1.25);
    el('dsg-reset-camera').onclick=()=>{resetCamera(el('dsg-canvas'));redraw();};
    renderLegend(el('dsg-legend'));syncControls();redraw();
}
