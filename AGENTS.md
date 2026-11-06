# AGENTS.md — MqttY (симулятор MQTT-брокеров и сетей)

Краткое руководство для старта разработки в этом репозитории.

## 1. О проекте

**MqttY** — симулятор MQTT-брокеров и сетей в модельном времени. Моделируются клиенты, брокеры, сетевые задержки, очереди, протокольные обмены без запуска реальных брокеров. Поддерживает профилирование через профили MqttBenchmark.

Репозиторий: `https://github.com/entityfx/mqtty` (ветка `develop`).

**Стек:** C#, .NET 6 (целевой фреймворк основных проектов), .NET 8 SDK (сборка), .NET 8 для Designer (Avalonia).

## 2. Структура репозитория

```
mqtty/
├── AGENTS.md
├── README.md
├── config/                 # примеры конфигураций экспериментов
├── doc/                    # документация, примеры, оптимизации
├── results/                # результаты экспериментов (не коммитить)
├── src/
│   └── EntityFX.MqttSimulator/
│       ├── EntityFX.MqttSimulator.Contracts/   # контракты, счётчики, опции
│       ├── EntityFX.MqttSimulator/             # ядро (сеть, узлы, симулятор)
│       ├── EntityFX.MqttY.Engines/             # движки: LoadBalancing, Relay, Federation
│       ├── EntityFX.MqttY.LoadBalancing.App/   # CLI: demo, run, matrix
│       ├── EntityFX.MqttY.MqttRelay.App/       # CLI: relay, калибровка
│       ├── EntityFX.MqttY.Federation.App/      # CLI: федерация
│       ├── EntityFX.MqttY.Benchmark.Compare/   # сравнительныйベンчмарк seq/par
│       ├── EntityFX.MqttY.Plugin.Mqtt/         # MQTT-плагин, профили брокеров
│       ├── EntityFX.MqttY.Plugin.Scenarios/    # сценарии и действия симуляции
│       ├── EntityFX.MqttY.Web.App/             # веб-интерфейс и API
│       ├── EntityFX.MqttY.Cli/                 # общий CLI
│       ├── EntityFX.MqttY.Designer/            # графический редактор (Avalonia, .NET 8)
│       ├── EntityFX.Tests.Integration/         # интеграционные тесты
│       └── EntityFX.Tests.Web/                 # веб-тесты
├── tools/                  # скрипты измерений, kampanей, анализа
├── utils/                  # утилиты разбора результатов
└── .gitignore
```

## 3. Окружение и prerequisites

- **.NET SDK 6.0** и **8.0** (установлены: `dotnet --list-sdks` покажет `6.0.136` и `8.0.121`)
- Целевой фреймворк основных проектов: `net6.0` (уже выведен из поддержки — предупреждения NETSDK1138, это ожидаемо)
- Designer требует .NET 8

Проверка окружения:
```bash
dotnet --version
dotnet --list-sdks
dotnet --list-runtimes
```

## 4. Сборка

Сборка проекта (пример — LoadBalancing.App):
```bash
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.LoadBalancing.App/EntityFX.MqttY.LoadBalancing.App.csproj -c Release --nologo
```

Сборка бенчмарка сравнения:
```bash
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --nologo
```

> Предостережение: в рабочей копии есть **незакоммиченные изменения** до вашего прихода (оптимизации `CounterClock.cs`, `GenericCounter.cs`, `ValueCounter.cs`, `Network.cs`, `NetworkSimulator.cs`, `Node.cs` и т.д.). Они не относятся к задаче и не должны попадать в коммит без согласия автора. Коммитьте только свои новые файлы.

## 5. Запуск экспериментов (бенчмарков)

### 5.1 Load Balancing — `demo` (идеальные брокеры, без профиля)
```bash
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.LoadBalancing.App/EntityFX.MqttY.LoadBalancing.App.csproj -c Release --no-build -- demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42 --output results/demo-run
```

### 5.2 Сравнительныйベンчмарк (seq vs par) — LB, Relay, Federation
Проект `EntityFX.MqttY.Benchmark.Compare`. Запуск:
```bash
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build
```

Выводит таблицу wall-clock времени для последовательного (`parallel=false`) и параллельного (`parallel=true`) режима по трём движкам. Результаты сохраняются во временную папку.

### 5.3 Интеграционные тесты
```bash
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo
```

## 6. Ключевые компоненты для разработкиベンчмарков

| Компонент | Путь | Назначение |
|---|---|---|
| `LoadBalancingOptions` | `Engines/LoadBalancing/Configuration/LoadBalancingOptions.cs` | Параметры LB-эксперимента (узлы, сеть, RPS, тики) |
| `LoadBalancingExperimentRunner` | `Engines/LoadBalancing/Execution/LoadBalancingExperimentRunner.cs` | Запуск симуляции, флаг `parallel` управляет `graph.Refresh(parallel, ...)` |
| `ExperimentInput` | `Engines/LoadBalancing/Inputs/ExperimentInput.cs` | Подготовка и валидация входных данных |
| `FederationOptions` / `FederationRunner` | `Engines/Federation/Configuration/`, `Engines/Federation/Execution/` | Параметры и запуск федерации |
| `MqttRelayApp` | `Engines/Relay/MqttRelayApp.cs` | Запуск relay-симуляции (`ExecuteSimulation(isParallel, ...)`) |
| `NetworkSettings` / `FederationNetworkOptions` | опции сети для LB и Federation |

Важно: флаг параллелизма **не** входит в `LoadBalancingOptions` / `FederationOptions` — он передаётся в метод раннера (`Run`, `RunMeasured`, `ExecuteSimulation`). Для сравнения seq/par вызывайте раннер дважды с `parallel=false` и `parallel=true`.

## 7. Правила коммитов

- Коммитьте **только свои** файлы (новые проекты, скрипты, статьи).
- Не трогайте незакоммиченные изменения, которые были в репозитории до вас (см. раздел 4).
- Результаты экспериментов в `results/` не коммитируются (`.gitignore`).
- bin/ и obj/ исключены из индекса.

## 8. Полезные команды

```bash
git status --short
dotnet build <путь к csproj> -c Release --nologo
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build
OUT="results/demo-$(date +%Y%m%d-%H%M%S)"
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.LoadBalancing.App/EntityFX.MqttY.LoadBalancing.App.csproj -c Release --no-build -- demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42 --output "$OUT"
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo
```

## 9. Документация

- `README.md` — быстрый запуск и обзор
- `doc/examples/` — примеры конфигураций (load-balancing, federation, mqtt-relay, broker-fidelity)
- `doc/optimization/` — результаты оптимизаций
- Внешняя документация: `github.com/EntityFX/mqtty-meta` (архитектура, калибровка, отчёты)
