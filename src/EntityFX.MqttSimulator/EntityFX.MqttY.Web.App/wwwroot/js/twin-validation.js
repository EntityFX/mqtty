import { api } from './api.js';
import { escapeHtml as esc, readDraft, saveDraft } from './workspace-state.js';
import { openResultsDialog } from './results-dialog.js';

export async function renderTwinValidation(app) {
    const [allPresets,initialReferences]=await Promise.all([api.get('/presets/brokers'),api.get('/twin-validations/references')]);
    const presets=allPresets.filter(p=>p.profilePath);let references=initialReferences;
    const draft=readDraft('mqtty-twin-validation')||{};
    app.innerHTML=`<div class="row justify-content-center"><section class="col-xl-8">
      <h4>Одиночная модель для статьи 1</h4>
      <p>Один брокер, один получатель, сообщения 16 байт. Каждый отправитель ждёт исхода предыдущей публикации. Измерительные сообщения отделены от прогрева по ID.</p>
      <div class="alert alert-info">Это модель MqttY. Настоящий брокер измеряется отдельно программой MqttBenchmark. Можно сравнить с калибровкой или выбрать отдельный натурный контроль после фиксации профиля. SHA проверяет целостность, а не истинность загруженных измерений.</div>
      <form id="twin-form" class="card p-3"><div class="row g-3">
      <div class="col-md-7"><label class="form-label" for="twin-name">Название опыта</label><input id="twin-name" name="name" class="form-control" required maxlength="200" value="${esc(draft.name||'Проверка одиночной модели')}"></div>
      <div class="col-md-5"><label class="form-label" for="twin-preset">Профиль брокера</label><select id="twin-preset" name="brokerPresetId" class="form-select" required>${presets.map(p=>`<option value="${esc(p.id)}" ${draft.brokerPresetId===p.id?'selected':''}>${esc(p.name)} (${esc(p.brokerType)})</option>`).join('')}</select></div>
      <div class="col-12"><label class="form-label" for="twin-reference">С чем сравнивать модель</label><select id="twin-reference" name="nativeControlReferenceId" class="form-select"></select><p id="twin-reference-details" class="small text-muted mt-2"></p><button type="button" id="twin-reference-apply" class="btn btn-outline-primary btn-sm">Применить условия натурного контроля</button></div>
      <details class="col-12"><summary>Загрузить сохранённый натурный контроль</summary><label for="twin-reference-file" class="form-label mt-2">Пакет JSON с тремя контрольными измерениями и их SHA</label><input id="twin-reference-file" type="file" accept=".json,application/json" class="form-control"><button type="button" id="twin-reference-import" class="btn btn-outline-secondary btn-sm mt-2">Загрузить контроль</button><p class="small text-muted mt-2">Web проверяет формат, происхождение и соответствие условий. Первичные натурные журналы должны быть отдельно пересчитаны; этот импорт не заменяет такую проверку.</p></details>
      ${[['publishers','Отправителей',16,1,1],['qos','QoS (1 или 2)',1,1,1],['seed','Seed',42,1,0],['warmupSeconds','Прогрев, модельных с',1,.01,0],['measurementSeconds','Нагрузка, модельных с',2,.01,.00001],['drainSeconds','Ожидание, модельных с',5,.01,.00001],['tickSeconds','Шаг времени, с',.00001,.00001,.0000001],['timeLimitSeconds','Предел расчёта на компьютере, с',600,1,1]].map(([key,label,value,step,min])=>`<div class="col-md-4"><label class="form-label" for="twin-${key}">${label}</label><input id="twin-${key}" name="${key}" type="number" step="${key.endsWith('Seconds')&&key!=='timeLimitSeconds'?'any':step}" min="${min}" ${key==='qos'?'max="2"':''} class="form-control" required value="${esc(draft[key]??value)}"></div>`).join('')}
      </div><p id="twin-ticks" class="small text-muted mt-3"></p>
      <div class="d-flex gap-2"><button type="button" id="twin-validate" class="btn btn-outline-primary">✓ Проверить</button><button id="twin-run" class="btn btn-success" type="submit">▶ Запустить модель</button></div>
      <p class="small text-muted mt-3 mb-0">После подачи наблюдение продолжается весь заданный срок. Дополнительные тики подключения и подписки сохраняются в результате. Очередь запускает опыт в отдельном процессе.</p></form>
      <div id="twin-feedback" class="mt-3" role="status"></div><div id="twin-job" class="mt-3"></div>
      </section></div>`;
    const form=app.querySelector('#twin-form'),feedback=app.querySelector('#twin-feedback');
    let timer,disposed=false;
    window.addEventListener('mqtty:before-navigation',()=>{disposed=true;clearTimeout(timer);},{once:true});
    const referenceSelect=app.querySelector('#twin-reference'),referenceDetails=app.querySelector('#twin-reference-details');
    function fillReferences(selected=''){referenceSelect.innerHTML='<option value="">Только калибровочный профиль — без отдельного натурного контроля</option>'+references.map(r=>`<option value="${esc(r.id)}" ${r.error?'disabled':''}>${esc(r.name)}${r.broker?' · '+esc(r.broker):''}${r.error?' — недоступен':''}</option>`).join('');referenceSelect.value=selected;}
    fillReferences(draft.nativeControlReferenceId||'');
    function selectedReference(){return references.find(r=>r.id===referenceSelect.value&&!r.error);}
    function values(){const data=Object.fromEntries(new FormData(form));for(const key of Object.keys(data))if(!['name','brokerPresetId','nativeControlReferenceId'].includes(key))data[key]=Number(data[key]);return data;}
    function update(){const data=values();saveDraft('mqtty-twin-validation',data);app.querySelector('#twin-ticks').textContent=['warmupSeconds','measurementSeconds','drainSeconds'].map((key,i)=>`${['Прогрев','Нагрузка','Ожидание'][i]}: ${data.tickSeconds>0?Math.round(data[key]/data.tickSeconds).toLocaleString('ru-RU'):'—'} тиков`).join(' · ');}
    function referenceInfo(){const r=selectedReference();referenceDetails.textContent=r?`${r.referenceRuns} натурных повтора · ${r.controlCampaignId} · SHA ${r.sha256}. Окна: ${r.workload.warmupSeconds} / ${r.workload.measurementSeconds} / ${r.workload.drainSeconds} с. Применение условий выполняется только по кнопке.`:'Отдельный натурный контроль не выбран. Критерии исходной сводки относятся к калибровке.';app.querySelector('#twin-reference-apply').disabled=!r;}
    referenceSelect.onchange=()=>{referenceInfo();update();};
    app.querySelector('#twin-reference-apply').onclick=()=>{const r=selectedReference();if(!r)return;for(const key of ['publishers','qos','warmupSeconds','measurementSeconds','drainSeconds'])form.elements[key].value=r.workload[key];update();feedback.textContent='Условия контроля применены. Выберите соответствующий профиль и нажмите «Проверить».';};
    app.querySelector('#twin-reference-import').onclick=async()=>{const file=app.querySelector('#twin-reference-file').files[0];if(!file){feedback.textContent='Выберите JSON натурного контроля.';return;}const button=app.querySelector('#twin-reference-import');button.disabled=true;try{const response=await fetch('/api/twin-validations/references',{method:'POST',headers:{'Content-Type':'application/json'},body:file});const value=await response.json();if(!response.ok)throw new Error(value.error||'Не удалось загрузить контроль.');references=await api.get('/twin-validations/references');if(disposed)return;fillReferences(value.id);referenceInfo();update();feedback.textContent=`Контроль загружен. SHA: ${value.sha256}. Условия модели не изменены; примените их явно.`;}catch(error){if(!disposed)feedback.textContent='Ошибка: '+error.message;}finally{button.disabled=false;}};
    form.oninput=update;referenceInfo();update();
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
        if(validateOnly)feedback.textContent=`Параметры корректны. Тики: ${result.warmupTicks} / ${result.measurementTicks} / ${result.drainTicks}. SHA профиля: ${result.profileSha256}. ${result.nativeControlReferenceSha256?'Отдельный натурный контроль: '+result.nativeControlReferenceSha256:'Только калибровка; натурный контроль не выбран.'}`;
        else{feedback.textContent='Модель поставлена в очередь. Это технический запуск, не новая натурная проверка.';saveDraft('mqtty-selected-simulation',result.id);clearTimeout(timer);observe(result.id);}
      }catch(error){if(!disposed)feedback.textContent='Ошибка: '+error.message;}
      finally{buttons.forEach(b=>b.disabled=false);referenceInfo();}
    }
    app.querySelector('#twin-validate').onclick=()=>send(true);
    form.onsubmit=event=>{event.preventDefault();send(false);};
}
