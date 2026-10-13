# MqttY

Дискретный симулятор сетей MQTT-клиентов, брокеров и relay приложений. Параметры поведения реальных брокеров поступают из внешней калибровки **v3** MqttBenchmark; topology, ticks и network capacity задаются отдельно.

[Индекс документации](doc/README.md) ведёт к калибровке, BrokerFidelity, смешанной сети MqttRelay, архитектуре и валидации. [Общий исследовательский маршрут](../diss/doc/README.md) связывает измерения и симуляцию.

Из корня mqtty, PowerShell 7 в Linux/Windows:

```powershell
dotnet run -c Release --project doc/examples/custom-network -- --ideal results/custom-ideal-001
```

Это компилируемый пример public API с одной MQTT-доставкой, без Designer. После получения полной матрицы 288 измерений используйте [calibrate](doc/calibration-v3.md) и [experiment](doc/network-experiments.md) с внешним broker-calibration.v3.json. Успешный smoke или calibration не доказывает fidelity; [критерии](doc/validation.md) оцениваются отдельно.
