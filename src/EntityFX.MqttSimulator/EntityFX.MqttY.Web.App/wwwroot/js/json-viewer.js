import { escapeHtml as esc } from './workspace-state.js';

export function highlightedJson(value) {
    const text=JSON.stringify(value,null,2);
    return text.replace(/"(?:\\.|[^"\\])*"\s*:|"(?:\\.|[^"\\])*"|\b(?:true|false|null)\b|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/g, token=>{
        const kind=token.startsWith('"')?(token.endsWith(':')?'key':'string'):/^(true|false|null)$/.test(token)?'literal':'number';
        return `<span class="json-${kind}">${esc(token)}</span>`;
    });
}

export function showJson(value,title='JSON') {
    document.getElementById('json-preview')?.remove();
    const dialog=document.createElement('dialog');dialog.id='json-preview';dialog.className='json-dialog';
    dialog.innerHTML=`<div class="d-flex justify-content-between gap-2"><h5>${esc(title)}</h5><button class="btn btn-outline-secondary btn-sm">Закрыть</button></div><pre class="json-code"><code>${highlightedJson(value)}</code></pre>`;
    dialog.querySelector('button').onclick=()=>dialog.close();dialog.addEventListener('close',()=>dialog.remove());
    document.body.append(dialog);dialog.showModal();
}

export async function previewJsonUrl(url,title) {
    const response=await fetch(url);if(!response.ok)throw new Error('Файл недоступен: '+response.status);
    const text=await response.text();if(text.length>5_000_000)throw new Error('Для просмотра выберите JSON до 5 МБ. Большой файл можно скачать.');
    showJson(JSON.parse(text),title);
}

export async function renderJsonViewer(app) {
    app.innerHTML='<h4>Просмотр JSON</h4><p>Откройте любой JSON: топологию, профиль, настройки или результат. Файл читается в браузере и не отправляется на сервер.</p><input id="json-file" type="file" accept=".json,application/json" class="form-control"><div id="json-error" role="alert"></div><pre class="json-code mt-3"><code id="json-content"></code></pre>';
    app.querySelector('#json-file').onchange=async e=>{try{const file=e.target.files[0];if(!file)return;if(file.size>5_000_000)throw new Error('Размер для просмотра — до 5 МБ.');app.querySelector('#json-content').innerHTML=highlightedJson(JSON.parse(await file.text()));app.querySelector('#json-error').textContent='';}catch(error){app.querySelector('#json-error').textContent=error.message;}};
}

// Scalar controls retain the original JSON types and structure, including unknown fields.
export function jsonParameterForm(container,initial) {
    const value=structuredClone(initial), controls=[];
    function render(parent,current,path) {
        if(current!==null&&typeof current==='object'){
            for(const [key,child] of Object.entries(current)){
                if(child!==null&&typeof child==='object'){
                    const details=document.createElement('details');details.className='mb-2';
                    const heading=document.createElement('summary');heading.textContent=key+(Array.isArray(child)?` (${child.length})`:'');details.append(heading);parent.append(details);render(details,child,[...path,key]);
                }else{
                    const label=document.createElement('label');label.className='form-label d-block small';label.textContent=key;
                    const input=document.createElement('input');input.className='form-control form-control-sm';
                    input.type=typeof child==='boolean'?'checkbox':typeof child==='number'?'number':'text';input.step='any';
                    if(input.type==='checkbox')input.checked=child;else input.value=child??'';
                    input.disabled=child===null;if(child===null)input.placeholder='null (не задано)';label.append(input);parent.append(label);controls.push({path:[...path,key],input,type:typeof child,original:child});
                }
            }
        }
    }
    container.replaceChildren();render(container,value,[]);
    return ()=>{const result=structuredClone(value);for(const {path,input,type,original} of controls){let owner=result;for(const key of path.slice(0,-1))owner=owner[key];const next=original===null?null:type==='boolean'?input.checked:type==='number'?Number(input.value):input.value;if(type==='number'&&!Number.isFinite(next))throw new Error('Число обязательно: '+path.join('.'));owner[path.at(-1)]=next;}return result;};
}
