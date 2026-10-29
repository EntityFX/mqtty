import { escapeHtml as esc } from './workspace-state.js';

export function renderNativeControlComparison(value) {
    if(!value)return '<div class="alert alert-warning" data-native-control-empty>Отдельный натурный контроль не выбран. Критерии исходной сводки сравнивают модель с калибровочным профилем.</div>';
    const names={rps:'Скорость завершений, публикаций/с',p99:'p99 публикации, мс',failure:'Неуспешные операции, %',loss:'Недоставка успешных публикаций, %'};
    const number=x=>x==null?'—':Number(x).toLocaleString('ru-RU',{maximumFractionDigits:6});
    const rows=Object.entries(value.metrics||{}).map(([key,m])=>{
        const fraction=key==='failure'||key==='loss';
        const metric=x=>number(x==null?null:x*(fraction?100:1));
        const error=x=>x==null?'—':number(x*100)+(fraction?' п.п.':'%');
        return `<tr><th>${esc(names[key]||key)}</th><td>${metric(m.reference)}</td><td>${metric(m.simulated)}</td><td>${error(m.error)}</td><td>${error(m.limit)}</td><td>${m.status==='passed'?'В допуске':m.status==='failed'?'Вне допуска':'Неприменимо'}</td></tr>`;
    }).join('');
    return `<section class="card p-3 mb-3" id="native-control-comparison"><h6>Натурный контроль — модель</h6><p class="mb-1">${esc(value.referenceName)} · ${value.referenceRuns} отдельных натурных повтора.</p><p class="small text-muted">Серия контроля: ${esc(value.controlCampaignId)}. Калибровка: ${esc(value.calibrationCampaignId)}.<br>SHA контроля: <span class="text-break">${esc(value.referenceSha256)}</span><br>SHA профиля: <span class="text-break">${esc(value.profileSha256)}</span></p><div class="alert ${value.allCriteriaPassed?'alert-success':'alert-warning'}">${value.allCriteriaPassed?'Все применимые показатели этой модели в заданных допусках.':'Есть расхождения, неприменимые показатели или неразрешённые исходы.'} Это вывод для данного запуска и условий.</div><div class="table-responsive"><table class="table table-sm"><thead><tr><th>Показатель</th><th>Контроль: среднее 3 опытов</th><th>Модель</th><th>Различие</th><th>Допуск</th><th>Результат</th></tr></thead><tbody>${rows}</tbody></table></div><p class="small mb-1">p99 контроля — среднее трёх отдельных квантилей, не квантиль объединённого потока. Для долей разность показана в процентных пунктах.</p><p class="small text-muted mb-0">${esc(value.scope)} ${esc(value.sourceVerification)}</p></section>`;
}
