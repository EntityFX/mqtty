// Один набор действий за раз: создание, свойства узла, связи или файлы.
export function configureWorkspace(app) {
    const sidebar=app.querySelector('.workspace-sidebar');
    const sections=[...sidebar.children].filter(e=>e.tagName==='DETAILS');
    const [create,timing,node,links,files]=sections;
    const commandbar=document.createElement('div');commandbar.className='workspace-commandbar card';
    const name=app.querySelector('#dsg-name');name.classList.add('workspace-name');name.setAttribute('aria-label','Название топологии');
    sidebar.querySelector('label[for="dsg-name"]')?.remove();
    commandbar.append(name,app.querySelector('#dsg-save'),app.querySelector('#dsg-start-sim'));
    const otherActions=sidebar.querySelector('.d-flex');if(otherActions)files.append(otherActions);
    app.querySelector('.workspace').before(commandbar);
    const tabs=document.createElement('div');tabs.className='workspace-tabs';tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','Задачи редактора');sidebar.prepend(tabs);
    const run=document.createElement('details');run.className='workspace-pane';run.open=true;run.innerHTML='<summary>Параметры запуска</summary><div class="control-grid"></div>';sidebar.append(run);
    const groups={create:[create],run:[run,timing],node:[node],links:[links],files:[files]};
    const labels={create:'✦ Создать',run:'▶ Запуск',node:'◉ Узел',links:'🔗 Связи',files:'▣ Файлы'};
    function show(key) {
      Object.entries(groups).forEach(([name,items])=>items.forEach(e=>{e.hidden=name!==key;}));
      tabs.querySelectorAll('button').forEach(b=>{b.classList.toggle('active',b.dataset.panel===key);b.setAttribute('aria-selected',String(b.dataset.panel===key));});
    }
    Object.entries(labels).forEach(([key,label])=>{const button=document.createElement('button');button.type='button';button.className='btn btn-sm';button.dataset.panel=key;button.setAttribute('role','tab');button.textContent=label;button.onclick=()=>show(key);tabs.append(button);});
    [create,node,links,files].forEach(e=>{e.open=true;e.classList.add('workspace-pane');});
    for(const id of ['dsg-broker','dsg-engine','dsg-qos','dsg-rps','dsg-duration','dsg-balancing','dsg-payload','dsg-seed'])run.lastElementChild.append(app.querySelector('#'+id).closest('label'));
    run.lastElementChild.insertAdjacentHTML('beforeend','<label>Повторов<select id="dsg-repeats" class="form-select form-select-sm"><option>1</option><option>3</option><option>5</option></select></label><label class="wide"><input id="dsg-all-qos" type="checkbox"> Серия по QoS 0, 1 и 2</label>');
    const note=document.createElement('p');note.className='small text-muted mt-2';note.textContent='Эти параметры применяются ко всему текущему графу при запуске. Создавать его заново не нужно.';run.append(note);
    show('create');
    return show;
}
