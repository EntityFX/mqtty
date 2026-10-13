[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CalibrationPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateSet('Aedes','Mosquitto','ActiveMQ','EMQX')][string]$Broker = 'Mosquitto',
    [ValidateSet(16,256)][int]$MessageBytes = 16,
    [ValidateRange(0,2)][int]$Qos = 1,
    [ValidateSet(1,16,64,128)][int]$Publishers = 1,
    [int]$Seed = 42,
    [string]$MqttYRepository = (Split-Path (Split-Path $PSScriptRoot)),
    [string]$MqttBenchmarkRepository = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot))) 'MqttBenchmark')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$calibrationFile = [IO.Path]::GetFullPath($CalibrationPath)
$outputFile = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputFile) { throw 'Choose a new config output file.' }
$calibration = Get-Content -Raw -LiteralPath $calibrationFile | ConvertFrom-Json -AsHashtable
if ($calibration.schemaVersion -ne 3 -or $calibration.artifactType -ne 'broker-calibration') { throw 'External calibration v3 is required.' }
$points = @($calibration.points | Where-Object {
    $_.broker -eq $Broker -and $_.messageBytes -eq $MessageBytes -and $_.qos -eq $Qos -and $_.publishers -eq $Publishers
})
if ($points.Count -ne 1) { throw 'Exactly one matching calibrated point is required.' }
$config = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'broker-fidelity.v3.json') | ConvertFrom-Json -AsHashtable
$experiment = $config.experiment
$experiment.brokerAssignmentMode = $Broker.Substring(0,1).ToLowerInvariant() + $Broker.Substring(1)
$experiment.clientsPerBroker = $Publishers
$experiment.payloadBytes = $MessageBytes
$experiment.publishQos = @('atMostOnce','atLeastOnce','exactlyOnce')[$Qos]
$experiment.subscribeQos = $experiment.publishQos
$experiment.offeredRps = $points[0].attemptedRps
$experiment.randomSeed = $Seed
$config.brokerProfilePath = $calibrationFile
$config.mqttYRepository = [IO.Path]::GetFullPath($MqttYRepository)
$config.mqttBenchmarkRepository = [IO.Path]::GetFullPath($MqttBenchmarkRepository)
[void][IO.Directory]::CreateDirectory((Split-Path $outputFile))
$stream = [IO.File]::Open($outputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json $config -Depth 30))
    $stream.Write($bytes,0,$bytes.Length)
} finally { $stream.Dispose() }
Write-Host "Config: $outputFile. Review network/tick parameters against measured RTT before claiming fidelity."
