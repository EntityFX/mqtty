import { api } from './api.js';
import { renderGraph, renderLegend, resetCamera } from './graph-renderer.js';
import { openBrokerDialog, closeBrokerDialog } from './broker-dialog.js';
import { escapeHtml as esc, readDraft, saveDraft, showMessage } from './workspace-state.js';

const states={Queued:'В очереди',Running:'Выполняется',Paused:'Пауза',Cancelling:'Останавливается',Cancelled:'Остановлена',Completed:'Завершена',Failed:'Ошибка',TimedOut:'Лимит времени',Interrupted:'Прервана'};
const terminal=s=>['Completed','Failed','Cancelled','TimedOut','Interrupted'].includes(s);
export async function renderMonitoring(app) {
    app.innerHTML=`<div class="workspace-heading"><h4>Мониторинг симуляций</h4><span class="small text-muted">Обновление каждые 2 секунды</span></div><div data-feedback role="status" hidden></div>
      <div class="workspace"><aside class="workspace-sidebar card p-3">
        <label for="mon-job" class="form-label">Запуск</label><select id="mon-job" class="form-select form-select-sm mb-2"></select>
        <div id="mon-status" class="mb-2"></div><div id="mon-actions" class="d-flex flex-wrap gap-1 mb-2"></div>
        <p class="small text-muted">Пауза сохраняет модель в памяти процесса. «Остановить» завершает запуск; «Повторить» создаёт новый запуск с теми же параметрами.</p>
        <label for="mon-node" class="form-label">Статистика узла</label><select id="mon-node" class="form-select form-select-sm"><option value="">Выберите узел…</option></select>
        <h6 class="mt-3">Процесс</h6><pre id="mon-process" class="small text-wrap"></pre>
        <details><summary>Файлы результатов</summary><div id="mon-files" class="d-grid gap-1 small"></div></details>
        <p class="small text-muted mt-3">Здесь показаны задания очереди Web. Внешние запуски CLI и натурные измерения MqttBenchmark не попадают в неё автоматически.</p>
      </aside><section class="workspace-main card p-3"><div class="graph-toolbar"><div id="mon-legend"></div><button id="mon-reset-camera" class="btn btn-light btn-sm">Вписать схему</button></div>
        <div id="mon-stats" class="stats-grid"></div><svg id="mon-canvas" aria-label="Мониторинг: нажмите на узел для статистики"></svg>
        <div id="mon-phase" class="small text-muted mt-2"></div><details class="mt-2"><summary>Все итоговые показатели</summary><pre id="mon-summary" class="small overflow-auto" style="max-height:360px"></pre></details>
      </section></div>`;
    const el=id=>app.querySelector('#'+id);
    let id=readDraft('mqtty-selected-simulation'), disposed=false, timer, graph=null, summaryId=null;
    const positions=new Map();
    const cleanup=()=>{disposed=true;clearTimeout(timer);closeBrokerDialog();};
    window.addEventListener('mqtty:before-navigation',cleanup,{once:true});
    renderLegend(el('mon-legend'));
    function choose(next) { id=next;saveDraft('mqtty-selected-simulation',id);positions.clear();summaryId=null;graph=null;el('mon-stats').innerHTML='';el('mon-summary').textContent='Итоги появятся после завершения.';el('mon-files').innerHTML='';resetCamera(el('mon-canvas')); }
    el('mon-job').onchange=e=>{choose(e.target.value);refresh();};
    el('mon-node').onchange=e=>{if(e.target.value)openBrokerDialog(id,e.target.value);};
    function draw() { if(!graph)return;renderGraph(el('mon-canvas'),graph,node=>openBrokerDialog(id,node),{draggable:true,onPositionChange:(node,m)=>positions.set(node,{x:m.x,y:m.y})}); }
    el('mon-reset-camera').onclick=()=>{resetCamera(el('mon-canvas'));draw();};
    function metric(label,value,unit='') { return `<div class="stat-card"><strong>${value==null?'—':typeof value==='number'?value.toLocaleString('ru-RU',{maximumFractionDigits:3}):esc(value)}${unit}</strong><span>${esc(label)}</span></div>`; }
    async function showResults(job) {
      if(!terminal(job.state)||summaryId===id)return;
      const selected=id;
      const files=await api.get(`/simulations/${id}/results`).catch(()=>({}));
      if(disposed||selected!==id)return;
      el('mon-files').innerHTML=Object.entries(files).map(([name,url])=>`<a href="${esc(url)}" download>${esc(name)}</a>`).join('')||'Итоговые файлы ещё не созданы.';
      if(!files['summary.json'])return;
      const summary=await api.get(`/simulations/${id}/results/summary.json`);
      if(disposed||selected!==id)return;
      summaryId=id;
      const delivered=summary.uniqueDeliveries??summary.delivery?.uniqueDelivered;
      const loss=summary.deliveryLoss??summary.delivery?.lossRate;
      el('mon-stats').innerHTML=metric('Попытки исходных публикаций',summary.apiAttempts)+metric('Завершённые исходные публикации',summary.sourceCompleted??summary.completedPublishes)+metric('Уникальные доставки',delivered)+metric('Недоставка',loss==null?null:loss*100,'%')+metric('Исходных публикаций / с',summary.sourceCompletedRps??summary.completedRps)+metric('p99 получения, мс',summary.receiverP99Ms??summary.delivery?.p99Ms)+metric('Повторные доставки',summary.duplicateDeliveries??summary.delivery?.duplicates)+metric('Отброшено из очереди',summary.queueDrops);
      el('mon-summary').textContent=JSON.stringify(summary,null,2);
    }
    async function refresh() {
      try {
        const list=await api.get('/simulations');if(disposed)return;
        if(!list.some(j=>j.id===id))choose(list[0]?.id||null);
        el('mon-job').innerHTML=list.map(j=>`<option value="${esc(j.id)}">${esc(j.engine)} · ${esc(states[j.state]||j.state)} · ${esc(j.id.slice(0,8))}</option>`).join('')||'<option>Запусков пока нет</option>';
        el('mon-job').value=id||'';
        if(!id){el('mon-status').textContent='Создайте топологию и запустите симуляцию.';return;}
        const selected=id;
        const [job,snapshot]=await Promise.all([api.get(`/simulations/${id}`),api.get(`/simulations/${id}/monitor`).catch(()=>null)]);
        if(disposed||selected!==id)return;
        el('mon-status').textContent=states[job.state]||job.state;
        const actions=[];
        if(job.state==='Running'&&job.processId)actions.push(['pause','⏸ Пауза','outline-warning']);
        if(job.state==='Paused')actions.push(['resume','▶ Продолжить','success']);
        if(['Queued','Running','Paused'].includes(job.state))actions.push(['stop','■ Остановить','outline-danger']);
        if(terminal(job.state))actions.push(['restart','↻ Повторить','primary']);
        el('mon-actions').innerHTML=actions.map(([a,t,c])=>`<button data-action="${a}" class="btn btn-${c} btn-sm">${t}</button>`).join('');
        el('mon-actions').querySelectorAll('[data-action]').forEach(b=>b.onclick=async()=>{b.disabled=true;try{const result=await api.post(`/simulations/${id}/${b.dataset.action}`);if(b.dataset.action==='restart')choose(result.id);await refresh();}catch(e){showMessage(app,e.message,true);}finally{b.disabled=false;}});
        el('mon-process').textContent=`PID: ${job.processId??'—'}\nПамять: ${((job.processWorkingSetBytes||0)/1024**2).toFixed(1)} МиБ\nCPU: ${(job.processCpuSeconds||0).toFixed(2)} с\n${job.error||''}`;
        el('mon-phase').textContent=`Этап: ${job.phase||'—'} · модельных шагов: ${job.totalTicks} · ошибок исполнения: ${job.errors}`;
        if(snapshot?.nodes?.length){
          graph={networks:[],nodes:[],links:snapshot.links||[]};
          snapshot.nodes.forEach((n,i)=>{if(!positions.has(n.id)){const angle=i*2*Math.PI/snapshot.nodes.length;positions.set(n.id,{x:450+280*Math.cos(angle),y:350+240*Math.sin(angle)});}const m={...n,...positions.get(n.id),name:n.name||n.id};if(n.type==='Network'){m.networkType=n.specification;graph.networks.push(m);}else graph.nodes.push(m);});
          const previous=el('mon-node').value;
          el('mon-node').innerHTML='<option value="">Выберите узел…</option>'+snapshot.nodes.map(n=>`<option value="${esc(n.id)}">${esc(n.name||n.id)} (${esc(n.type)})</option>`).join('');el('mon-node').value=previous;draw();
        }
        await showResults(job);
      }catch(e){if(!disposed)showMessage(app,'Не удалось обновить мониторинг: '+e.message,true);}
    }
    // Sequential requests only; navigation disposes the timer.
    async function poll(){await refresh();if(!disposed)timer=setTimeout(poll,2000);}
    await poll();
}
