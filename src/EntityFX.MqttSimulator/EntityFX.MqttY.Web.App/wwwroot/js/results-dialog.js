import { api } from './api.js';
import { previewJsonUrl } from './json-viewer.js';
import { escapeHtml as esc } from './workspace-state.js';

const labels={status:'Состояние',measurementSeconds:'Окно нагрузки, модельных с',tickMilliseconds:'Шаг модели, мс',scheduledPublishes:'Запланировано публикаций',apiAttempts:'Попыток публикации',sourceCompleted:'Завершено исходных публикаций',sourceCompletedRps:'Завершений исходных публикаций / с',expectedDeliveries:'Ожидаемые доставки',uniqueDeliveries:'Уникальные доставки',uniqueBeforeDrain:'Доставки во время нагрузки',uniqueDuringDrain:'Доставки во время ожидания',deliveryLoss:'Доля недоставки',duplicateDeliveries:'Повторные доставки',forwardAttempts:'Попытки пересылки',forwardAccepted:'Принятые пересылки',queueDrops:'Отброшено из очередей',suppressedDuplicates:'Подавленные повторы',publishP99Ms:'p99 публикации, мс',receiverP99Ms:'p99 получения, мс',publishP50Ms:'p50 публикации, мс',publishP95Ms:'p95 публикации, мс',receiverP50Ms:'p50 получения, мс',receiverP95Ms:'p95 получения, мс',backpressureUnsent:'Не подано из-за занятости отправителя'};

function distribution(title,bins,tickMs) {
    const entries=Object.entries(bins||{}).map(([tick,count])=>[Number(tick)*tickMs,Number(count)]).filter(([t,c])=>Number.isFinite(t)&&Number.isFinite(c)&&c>0).sort((a,b)=>a[0]-b[0]);
    const total=entries.reduce((sum,p)=>sum+p[1],0);
    if(!total)return `<div class="col-md-6"><h6>${esc(title)}</h6><p class="text-muted">Нет применимых наблюдений.</p></div>`;
    const max=entries.at(-1)[0]||1;let cumulative=0;
    const samples=entries.map(([time,count])=>{cumulative+=count;return [40+time/max*480,210-cumulative/total*170];});
    const stride=Math.max(1,Math.ceil(samples.length/400));
    const points=samples.filter((_,i)=>i%stride===0||i===samples.length-1).map(p=>p.join(',')).join(' ');
    return `<div class="col-md-6"><h6>${esc(title)}</h6><svg viewBox="0 0 560 260" role="img" aria-label="${esc(title)}"><path d="M40 30V210H530" fill="none" stroke="#6b7788"/><text x="8" y="40" font-size="12">100%</text><text x="22" y="214" font-size="12">0</text><text x="40" y="235" font-size="12">0 мс</text><text x="435" y="235" font-size="12">${max.toFixed(3)} мс</text><polyline points="${points}" fill="none" stroke="#1467c0" stroke-width="2" vector-effect="non-scaling-stroke"/></svg><p class="small text-muted">Накопленная доля наблюдений; событий ${total.toLocaleString('ru-RU')}.</p></div>`;
}

export async function openResultsDialog(id) {
    let element=document.getElementById('resultsModal');
    if(!element){element=document.createElement('div');element.id='resultsModal';element.className='modal fade';element.tabIndex=-1;element.innerHTML='<div class="modal-dialog modal-xl modal-dialog-scrollable"><div class="modal-content"><div class="modal-header"><h5 class="modal-title">Результаты запуска</h5><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрыть"></button></div><div class="modal-body"></div></div></div>';document.body.append(element);}
    element.dataset.simulationId=id;
    const body=element.querySelector('.modal-body');body.textContent='Загрузка результатов…';bootstrap.Modal.getOrCreateInstance(element).show();
    try {
        const [job,files]=await Promise.all([api.get('/simulations/'+id),api.get('/simulations/'+id+'/results').catch(()=>({}))]);
        const summary=files['summary.json']?await api.get('/simulations/'+id+'/results/summary.json'):null;
        const hist=files['histograms.json']?await api.get('/simulations/'+id+'/results/histograms.json'):null;
        if(element.dataset.simulationId!==id)return;
        const date=value=>value?new Date(value).toLocaleString('ru-RU'):'—';
        const elapsed=job.startedAt?((new Date(job.finishedAt||Date.now())-new Date(job.startedAt))/1000).toFixed(2)+' с':'—';
        const metrics=summary?Object.entries(summary).filter(([,v])=>v===null||['string','number','boolean'].includes(typeof v)):[];
        const tick=job.tickMilliseconds??summary?.tickMilliseconds??hist?.tickMilliseconds??0.1;
        const phases=summary?.phases||[];
        const maxEnd=Math.max(1,...phases.map(p=>p.endTick||0));
        body.innerHTML=`<div class="d-flex gap-2 mb-3" role="tablist"><button class="btn btn-primary btn-sm" data-result-tab="summary">Статистика</button><button class="btn btn-outline-primary btn-sm" data-result-tab="charts">Графики</button><button class="btn btn-outline-primary btn-sm" data-result-tab="files">Файлы (${Object.keys(files).length})</button></div>
          <section data-result-panel="summary"><div class="row"><div class="col-md-6"><dl class="row small"><dt class="col-5">Запуск</dt><dd class="col-7">${esc(id)}</dd><dt class="col-5">Начало</dt><dd class="col-7">${date(job.startedAt)}</dd><dt class="col-5">Окончание</dt><dd class="col-7">${date(job.finishedAt)}</dd><dt class="col-5">Прошло на компьютере</dt><dd class="col-7">${elapsed}</dd><dt class="col-5">Модельных тиков</dt><dd class="col-7">${job.totalTicks}</dd><dt class="col-5">Шагов</dt><dd class="col-7">${job.step}</dd><dt class="col-5">Движок / статус</dt><dd class="col-7">${esc(job.engine)} / ${esc(job.state)}</dd></dl></div><div class="col-md-6"><p class="small text-muted">Время на компьютере и время модели — разные величины. Время от начала до окончания включает паузы; это не чистое время CPU.</p>${job.error?`<div class="alert alert-warning">${esc(job.error)}</div>`:''}</div></div><h6>Итоговые показатели</h6><table class="table table-sm table-striped"><tbody>${metrics.map(([key,value])=>`<tr><th>${esc(labels[key]||key)}</th><td>${value==null?'—':esc(value)}</td></tr>`).join('')||'<tr><td>Итог ещё не сохранён.</td></tr>'}</tbody></table></section>
          <section data-result-panel="charts" hidden><div class="row">${distribution('Задержка завершения публикации',hist?.publish??summary?.publishHistogramTicks??summary?.publishLatencyHistogramTicks,tick)}${distribution('Задержка получения сообщения',hist?.receiver??summary?.receiverHistogramTicks,tick)}</div><h6>Фазы в модельном времени</h6><div class="small">${phases.map(p=>`<div class="mb-2"><span>${esc(p.name??p.phase??'Этап')}: ${p.endTick-p.startTick} тиков · ${((p.endTick-p.startTick)*tick/1000).toFixed(4)} модельной с</span><div style="height:12px;background:#e8edf3"><div style="height:100%;background:#1467c0;margin-left:${p.startTick/maxEnd*100}%;width:${Math.max(.2,(p.endTick-p.startTick)/maxEnd*100)}%"></div></div></div>`).join('')||'Фазы не сохранены.'}</div></section>
          <section data-result-panel="files" hidden><p class="small text-muted">Скачиваются исходные файлы данного запуска; прежние результаты не перезаписываются.</p><div class="list-group">${Object.entries(files).map(([name,url])=>`<div class="list-group-item d-flex gap-2 align-items-center"><a class="flex-grow-1" href="${esc(url)}" download>⇩ ${esc(name)}</a>${name.endsWith('.json')?`<button class="btn btn-outline-primary btn-sm" data-json-preview="${esc(url)}" data-title="${esc(name)}">Просмотр</button>`:''}</div>`).join('')||'Файлов пока нет.'}</div></section>`;
        body.querySelectorAll('[data-json-preview]').forEach(button=>button.onclick=async()=>{try{await previewJsonUrl(button.dataset.jsonPreview,button.dataset.title);}catch(error){button.textContent=error.message;}});
        body.querySelectorAll('[data-result-tab]').forEach(button=>button.onclick=()=>{body.querySelectorAll('[data-result-panel]').forEach(panel=>panel.hidden=panel.dataset.resultPanel!==button.dataset.resultTab);body.querySelectorAll('[data-result-tab]').forEach(b=>{b.classList.toggle('btn-primary',b===button);b.classList.toggle('btn-outline-primary',b!==button);});});
    }catch(error){body.textContent='Не удалось загрузить результаты: '+error.message;}
}
export function closeResultsDialog(){const element=document.getElementById('resultsModal');if(element)bootstrap.Modal.getInstance(element)?.hide();}
