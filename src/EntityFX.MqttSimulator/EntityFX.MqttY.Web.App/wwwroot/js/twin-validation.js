import { api } from './api.js';
import { escapeHtml as esc, readDraft, saveDraft } from './workspace-state.js';
import { openResultsDialog } from './results-dialog.js';

export async function renderTwinValidation(app) {
    const presets=(await api.get('/presets/brokers')).filter(p=>p.profilePath);
    const draft=readDraft('mqtty-twin-validation')||{};
    app.innerHTML=`<div class="row justify-content-center"><section class="col-xl-8">
      <h4>Одиночная модель для статьи 1</h4>
      <p>Один брокер, один получатель, сообщения 16 байт. Каждый отправитель ждёт исхода предыдущей публикации. Измерительные сообщения отделены от прогрева по ID.</p>
      <div class="alert alert-info">Это модель MqttY. Настоящий брокер измеряется отдельно программой MqttBenchmark. Критерии внутри результата сравнивают с исходным профилем; они не означают новую независимую проверку точности.</div>
      <form id="twin-form" class="card p-3"><div class="row g-3">
      <div class="col-md-7"><label class="form-label" for="twin-name">Название опыта</label><input id="twin-name" name="name" class="form-control" required maxlength="200" value="${esc(draft.name||'Проверка одиночной модели')}"></div>
      <div class="col-md-5"><label class="form-label" for="twin-preset">Профиль брокера</label><select id="twin-preset" name="brokerPresetId" class="form-select" required>${presets.map(p=>`<option value="${esc(p.id)}" ${draft.brokerPresetId===p.id?'selected':''}>${esc(p.name)} (${esc(p.brokerType)})</option>`).join('')}</select></div>
      ${[['publishers','Отправителей',16,1,1],['qos','QoS (1 или 2)',1,1,1],['seed','Seed',42,1,0],['warmupSeconds','Прогрев, модельных с',1,.01,0],['measurementSeconds','Нагрузка, модельных с',2,.01,.00001],['drainSeconds','Ожидание, модельных с',5,.01,.00001],['tickSeconds','Шаг времени, с',.00001,.00001,.0000001],['timeLimitSeconds','Предел расчёта на компьютере, с',600,1,1]].map(([key,label,value,step,min])=>`<div class="col-md-4"><label class="form-label" for="twin-${key}">${label}</label><input id="twin-${key}" name="${key}" type="number" step="${key.endsWith('Seconds')&&key!=='timeLimitSeconds'?'any':step}" min="${min}" ${key==='qos'?'max="2"':''} class="form-control" required value="${esc(draft[key]??value)}"></div>`).join('')}
      </div><p id="twin-ticks" class="small text-muted mt-3"></p>
      <div class="d-flex gap-2"><button type="button" id="twin-validate" class="btn btn-outline-primary">✓ Проверить</button><button id="twin-run" class="btn btn-success" type="submit">▶ Запустить модель</button></div>
      <p class="small text-muted mt-3 mb-0">После подачи наблюдение продолжается весь заданный срок. Дополнительные тики подключения и подписки сохраняются в результате. Очередь запускает опыт в отдельном процессе.</p></form>
      <div id="twin-feedback" class="mt-3" role="status"></div><div id="twin-job" class="mt-3"></div>
      </section></div>`;
    const form=app.querySelector('#twin-form'),feedback=app.querySelector('#twin-feedback');
    let timer,disposed=false;
    window.addEventListener('mqtty:before-navigation',()=>{disposed=true;clearTimeout(timer);},{once:true});
    function values(){const data=Object.fromEntries(new FormData(form));for(const key of Object.keys(data))if(!['name','brokerPresetId'].includes(key))data[key]=Number(data[key]);return data;}
    function update(){const data=values();saveDraft('mqtty-twin-validation',data);app.querySelector('#twin-ticks').textContent=['warmupSeconds','measurementSeconds','drainSeconds'].map((key,i)=>`${['Прогрев','Нагрузка','Ожидание'][i]}: ${data.tickSeconds>0?Math.round(data[key]/data.tickSeconds).toLocaleString('ru-RU'):'—'} тиков`).join(' · ');}
    form.oninput=update;update();
    async function observe(id){
      if(disposed)return;
      try {
        const job=await api.get('/simulations/'+id);if(disposed)return;
        const target=app.querySelector('#twin-job');
        target.innerHTML=`<div class="card p-3"><strong>${esc(job.name)} · ${esc(job.state)}</strong><p class="mb-2">Этап: ${esc(job.phase||'ожидание')} · тиков: ${job.totalTicks} · шагов: ${job.step}</p>${job.error?`<pre class="text-wrap">${esc(job.error)}</pre>`:''}<div><button id="twin-results" class="btn btn-outline-primary btn-sm">Результаты, фазы и файлы</button> <a href="#/simulations" class="btn btn-light btn-sm">Мониторинг</a></div></div>`;
        target.querySelector('#twin-results').onclick=()=>openResultsDialog(id);
        if(['Queued','Running','Paused','Cancelling'].includes(job.state))timer=setTimeout(()=>observe(id),1500);
      }catch(error){if(!disposed)feedback.textContent=error.message;}
    }
    async function send(validateOnly){
      if(!form.reportValidity())return;
      const buttons=form.querySelectorAll('button');buttons.forEach(b=>b.disabled=true);feedback.textContent='Проверка входов…';
      try{const result=await api.post('/twin-validations',{...values(),validateOnly});if(disposed)return;
        if(validateOnly)feedback.textContent=`Параметры корректны. Тики: ${result.warmupTicks} / ${result.measurementTicks} / ${result.drainTicks}. SHA профиля: ${result.profileSha256}`;
        else{feedback.textContent='Модель поставлена в очередь. Это технический запуск, не новая натурная проверка.';saveDraft('mqtty-selected-simulation',result.id);clearTimeout(timer);observe(result.id);}
      }catch(error){if(!disposed)feedback.textContent='Ошибка: '+error.message;}
      finally{buttons.forEach(b=>b.disabled=false);}
    }
    app.querySelector('#twin-validate').onclick=()=>send(true);
    form.onsubmit=event=>{event.preventDefault();send(false);};
}
