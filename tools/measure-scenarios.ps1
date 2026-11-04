# measure-scenarios.ps1 — измерение wall-clock времени эталонных сценариев MqttY
# Использование: powershell -File tools\measure-scenarios.ps1 -Runs 3
param(
    [int]$Runs = 3,
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot | Split-Path

$scenarios = @(
    @{
        Name = "LoadBalancing"
        Exe  = Join-Path $root "src\EntityFX.MqttSimulator\EntityFX.MqttY.LoadBalancing.App\bin\Release\net6.0\EntityFX.MqttY.LoadBalancing.App.exe"
        Args = @("demo", "--brokers", "3", "--mode", "roundRobin", "--publishers", "12", "--qos", "1", "--seconds", "1", "--seed", "42")
    },
    @{
        Name = "Federation"
        Exe  = Join-Path $root "src\EntityFX.MqttSimulator\EntityFX.MqttY.Federation.App\bin\Release\net6.0\EntityFX.MqttY.Federation.App.exe"
        Args = @("demo", "--brokers", "3", "--topology", "ring", "--mode", "federation", "--publishers", "12", "--qos", "1", "--rps", "1000", "--seconds", "0.2", "--seed", "42")
    },
    @{
        Name = "MqttRelay (original)"
        Exe  = Join-Path $root "src\EntityFX.MqttSimulator\EntityFX.MqttY.MqttRelay.App\bin\Release\net6.0\EntityFX.MqttY.MqttRelay.App.exe"
        Args = @("original")
    }
)

$results = @()
foreach ($scenario in $scenarios) {
    $times = @()
    for ($i = 1; $i -le $Runs; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $proc = Start-Process -FilePath $scenario.Exe -ArgumentList $scenario.Args -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\mqttty-stdout.txt" -RedirectStandardError "$env:TEMP\mqttty-stderr.txt"
        $null = $proc.Handle  # materialize the process handle so ExitCode is populated after exit
        $proc.WaitForExit()
        $sw.Stop()
        if ($proc.ExitCode -ne 0) {
            Write-Host ("[{0}] Прогон {1}: ОШИБКА (exit code {2})" -f $scenario.Name, $i, $proc.ExitCode) -ForegroundColor Red
            Get-Content "$env:TEMP\mqttty-stderr.txt" -Tail 5 | Write-Host
        }
        $times += $sw.Elapsed.TotalSeconds
        Write-Host ("[{0}] Прогон {1}: {2:F2} c" -f $scenario.Name, $i, $sw.Elapsed.TotalSeconds)
    }
    $avg = ($times | Measure-Object -Average).Average
    $std = [Math]::Sqrt((($times | ForEach-Object { [Math]::Pow($_ - $avg, 2) } | Measure-Object -Sum).Sum) / $times.Count)
    Write-Host ("[{0}] Среднее: {1:F2} c, ст.откл: {2:F2} c" -f $scenario.Name, $avg, $std)
    Write-Host ""
    $results += [PSCustomObject]@{ Scenario = $scenario.Name; AvgSeconds = [Math]::Round($avg, 2); StdSeconds = [Math]::Round($std, 2); Times = ($times -join ", ") }
}

if ($OutputFile -ne "") {
    $results | Format-Table -AutoSize | Out-String -Width 200 | Out-File $OutputFile -Encoding UTF8
    Write-Host "Результаты сохранены в $OutputFile"
}
