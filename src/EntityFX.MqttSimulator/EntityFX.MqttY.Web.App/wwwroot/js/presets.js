import { api } from './api.js';
import { navigate } from './router.js';
import { escapeHtml as esc, saveDraft, showMessage } from './workspace-state.js';

export async function renderPresets(app) {
    app.innerHTML=`<h4>Пресеты и сохранённые топологии</h4><p class="text-muted">Пресет задаёт параметры новых запусков. Изменения не переписывают уже выполненные опыты и их снимки профилей.</p><div data-feedback role="status" hidden></div>
      <div class="btn-group mb-3" role="group" aria-label="Каталог"><button data-tab="brokers" class="btn btn-outline-primary active">Брокеры</button><button data-tab="topologies" class="btn btn-outline-primary">Шаблоны топологий</button><button data-tab="saved" class="btn btn-outline-primary">Сохранённые графы</button></div>
      <div class="row g-3"><section class="col-lg-6"><div class="card p-3"><div id="pr-list"></div></div></section><section class="col-lg-6"><div class="card p-3" id="pr-editor">Выберите запись для редактирования или создайте новую.</div></section></div>`;
    const el=id=>app.querySelector('#'+id);
    let tab='brokers', list=[], selected=null;
    const endpoint=()=>tab==='saved'?'/topologies':'/presets/'+tab;
    async function refresh() {
      list=await api.get(endpoint());if(!app.isConnected)return;
      el('pr-list').innerHTML=`<button id="pr-add" class="btn btn-success btn-sm mb-3">＋ ${tab==='saved'?'Создать в редакторе':'Новый пресет'}</button><div class="list-group">${list.map(item=>`<div class="list-group-item d-flex align-items-center justify-content-between gap-2"><div><strong>${esc(item.name)}</strong><div class="small text-muted">${esc(item.brokerType||item.engine||item.spec?.engine||'Граф')} · ${esc(item.id)}</div></div><button class="btn btn-outline-primary btn-sm" data-edit="${esc(item.id)}">✎ Изменить</button></div>`).join('')||'<p class="text-muted">Записей пока нет</p>'}</div>`;
      el('pr-add').onclick=()=>{if(tab==='saved'){navigate('/designer');return;}edit(null);};
      el('pr-list').querySelectorAll('[data-edit]').forEach(b=>b.onclick=()=>edit(list.find(p=>p.id===b.dataset.edit)));
    }
    function field(id,label,value,type='text') {return `<label class="form-label w-100">${esc(label)}<input id="${id}" type="${type}" class="form-control form-control-sm" value="${esc(value)}"></label>`;}
    function select(id,label,value,values) {return `<label class="form-label w-100">${esc(label)}<select id="${id}" class="form-select form-select-sm">${values.map(([v,t])=>`<option value="${esc(v)}" ${v===value?'selected':''}>${esc(t)}</option>`).join('')}</select></label>`;}
    function edit(item) {
      selected=item?structuredClone(item):{};
      if(tab==='saved'){
        saveDraft('mqtty-editor-transfer',selected);navigate('/designer');return;
      }
      const broker=tab==='brokers';
      el('pr-editor').innerHTML=`<h5>${item?'Редактировать':'Создать'} ${broker?'пресет брокера':'шаблон топологии'}</h5><form id="pr-form">
        ${field('pr-name','Название',selected.name||'Новый пресет')}
        ${broker?select('pr-type','Реализация брокера',selected.brokerType||'Mosquitto',['Mosquitto','Aedes','ActiveMQ','EMQX'].map(t=>[t,t]))+field('pr-profile','Путь к профилю в каталоге данных (пусто — идеальная модель)',selected.profilePath||'')+field('pr-description','Описание',selected.description||''):
          select('pr-kind','Форма',selected.kind||'line',[['line','Линия'],['ring','Кольцо'],['star','Звезда']])+select('pr-engine','Способ объединения',selected.engine||'relay',[['relay','MqttRelay'],['federation','Федерация'],['loadBalancing','Балансировщик']])+field('pr-count','Брокеров',selected.brokers??(selected.kind==='ring'?selected.ringLength:selected.kind==='star'?selected.rays+1:selected.chainLength)??3,'number')+field('pr-broker','ID пресета брокера',selected.brokerPresetId||'ideal-demo')+field('pr-clients','Отправителей на брокер',selected.clientsPerBroker??2,'number')+field('pr-rps','Сообщений в секунду, всего',selected.offeredRps??1000,'number')+select('pr-qos','QoS',String(selected.qos??1),[['0','0'],['1','1'],['2','2']])}
        <details class="my-2"><summary>Все параметры JSON</summary><p class="small text-muted">Здесь можно изменить дополнительные параметры. Поля формы выше имеют приоритет.</p><textarea id="pr-json" class="form-control font-monospace" rows="8">${esc(JSON.stringify(selected,null,2))}</textarea></details>
        <div class="d-flex gap-2 mt-3"><button class="btn btn-primary btn-sm" type="submit">💾 Сохранить изменения</button><button id="pr-copy" class="btn btn-outline-success btn-sm" type="button">⧉ Сохранить копию</button>${item?'<button id="pr-delete" class="btn btn-outline-danger btn-sm" type="button">✕ Удалить</button>':''}</div></form>`;
      async function save(copy) {
        try {
          let value=JSON.parse(el('pr-json').value);value.name=el('pr-name').value.trim();if(!value.name)throw new Error('Укажите название.');
          if(broker){value.brokerType=el('pr-type').value;value.profilePath=el('pr-profile').value.trim();value.description=el('pr-description').value;}
          else{const count=Number(el('pr-count').value);if(!Number.isInteger(count)||count<2||count>16)throw new Error('Укажите от 2 до 16 брокеров.');value.kind=el('pr-kind').value;value.engine=el('pr-engine').value;value.brokers=count;value.chainLength=value.kind==='star'?1:count;value.rays=count-1;value.ringLength=count;value.brokerPresetId=el('pr-broker').value;value.clientsPerBroker=Number(el('pr-clients').value);value.offeredRps=Number(el('pr-rps').value);value.qos=Number(el('pr-qos').value);value.warmupTicks??=100;value.measurementTicks??=2000;value.drainTimeoutTicks??=10000;}
          if(copy){delete value.id;value.name='Копия '+value.name;}else if(selected.id)value.id=selected.id;
          const result=value.id?await api.put(endpoint()+'/'+encodeURIComponent(value.id),value):await api.post(endpoint(),value);
          await refresh();edit(result);showMessage(app,'Изменения сохранены. Уже запущенные опыты не изменены.');
        }catch(error){showMessage(app,'Не удалось сохранить: '+error.message,true);}
      }
      el('pr-form').onsubmit=e=>{e.preventDefault();save(false);};
      el('pr-copy').onclick=()=>save(true);
      if(item)el('pr-delete').onclick=async()=>{if(!confirm('Удалить выбранный пресет?'))return;try{await api.del(endpoint()+'/'+encodeURIComponent(item.id));el('pr-editor').textContent='Запись удалена.';await refresh();}catch(e){showMessage(app,e.message,true);}};
    }
    app.querySelectorAll('[data-tab]').forEach(b=>b.onclick=async()=>{tab=b.dataset.tab;app.querySelectorAll('[data-tab]').forEach(t=>t.classList.toggle('active',t===b));el('pr-editor').textContent='Выберите запись для редактирования.';try{await refresh();}catch(e){showMessage(app,e.message,true);}});
    await refresh();
}
