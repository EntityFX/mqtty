# Учёт транспортных ожиданий без сканирования каждого tick

`Node.Refresh` увеличивает один счётчик refresh вместо обхода всех ожидающих
ответов. Запись хранит значение счётчика на момент создания; прежний порог
600000 вызовов refresh проверяется при чтении ответа. Повторные refresh одного
graph tick и сброс узла сохраняют прежнюю семантику таймаута. Этот порог здесь
не заменён настройкой `ReceiveWaitPeriod`.

MQTT client сохраняет общие ожидания для Connect/Subscribe и неизвестных
расширений, сохраняя старые blocking API. Publish/PubAck/PubRec/PubRel/PubComp обслуживаются MQTT
state machine, а не `Node.WaitResponse`. MQTT broker не вызывает этот API и
не создаёт такие записи. Generic nodes по умолчанию сохраняют прежний учёт.
Диагностический ReceiveQueue теперь не включает фиктивные MQTT ожидания;
научные broker metrics, latency histograms и receiver receipts проверяются
сравнением с исходной версией.

В кампании `two-machine-20261014-1340-linux-local` оптимизация была разрешена
пользователем после 72 успешных калибровочных измерений Aedes, до завершения
первой симуляции и до независимого контроля. Натурный benchmark остаётся
на commit3a68946. Профиль Aedes и transport остаются неизменными. Для всех
последующих профилей сохраняется исходный calibrator/source commit55a87f1;
simulator provenance содержит новую версию движка. `tools/campaign/resume-optimized.py`
разделяет эти две роли. Переход версии, исходные конфигурации и прерванный run
сохраняются в evidence; исходные campaign identities не объединяются.
