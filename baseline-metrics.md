# Метрики производительности MqttY (ход оптимизации)

**Дата:** 2026-11-01  
**Платформа:** .NET 6 (SDK 6.0.428), Release сборка  
**Оборудование:** локальная рабочая станция

## Базовая линия (до оптимизации)

| Сценарий | Команда | Конфигурация | Wall-clock | Статус |
|----------|---------|-------------|------------|--------|
| LoadBalancing | `demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42` | 3 брокера, roundRobin, 12 publishers, QoS 1, 1 сек моделирования | 9.28 с | completed |
| Federation | `demo --brokers 3 --topology ring --mode federation --publishers 12 --qos 1 --rps 1000 --seconds 0.2 --seed 42` | 3 брокера, ring, federation, 12 publishers, QoS 1, 1000 RPS, 0.2 сек | 6.25 с | completed |
| MqttRelay (original) | `original` | 4 брокера, 15 клиентов, 131 repeats, refreshStrategy=0, 8 байт, TickPeriod=0.1ms | 24.81 с | completed |

## После Этапа 2 (мониторинг выключен)

| Сценарий | Wall-clock | Ускорение vs baseline |
|----------|------------|----------------------|
| LoadBalancing | 6.85 с | 1.36× |
| Federation | 4.30 с | 1.45× |
| MqttRelay (original) | 21.52 с | 1.15× |

## Целевой прирост

- **Цель:** 3–5× ускорение wall-clock времени
- **Ограничение:** точность симуляции сохраняется (delivery rate, latency quantiles, ошибки — без изменения)

## Гипотезы для проверки (H1–H5)

1. **H1 (Мониторинг):** `NetworkLogger.Push()` доминирует по CPU — **ПОДТВЕРЖДЕНА (Этап 2)**
2. **H2 (Последовательный Refresh):** стратегия 0 по умолчанию — в процессе (Этап 3)
3. **H3 (Очереди):** поэлементное `TryDequeue` — запланировано (Этап 4)
4. **H4 (Busy-wait):** циклы `Drive`/`Until` — запланировано (Этап 4)
5. **H5 (GC):** аллокации объектов на каждый пакет — запланировано (Этап 5)
