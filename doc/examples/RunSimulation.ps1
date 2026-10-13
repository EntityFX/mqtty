[CmdletBinding()]
param(
    [string]$ObservationsPath,
    [Parameter(Mandatory)][string]$CalibrationPath,
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Aedes','Mosquitto','ActiveMQ','EMQX')][string]$Broker = 'Mosquitto',
    [ValidateSet(16,256)][int]$MessageBytes = 16,
    [ValidateRange(0,2)][int]$Qos = 1,
    [ValidateSet(1,16,64,128)][int]$Publishers = 1,
    [int]$Seed = 42,
    [string]$SimulatorCliPath,
    [string]$MqttBenchmarkRepository = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot))) 'MqttBenchmark')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$calibration = [IO.Path]::GetFullPath($CalibrationPath)
$config = [IO.Path]::GetFullPath($ConfigPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$observations = if ($ObservationsPath) { [IO.Path]::GetFullPath($ObservationsPath) } else { $null }
$benchmark = [IO.Path]::GetFullPath($MqttBenchmarkRepository)
$project = Join-Path $root 'src/EntityFX.MqttSimulator/EntityFX.MqttY.MqttRelay.App'
$cli = if ($SimulatorCliPath) { [IO.Path]::GetFullPath($SimulatorCliPath) } else { $null }
if ($cli -and (-not (Test-Path -LiteralPath $cli -PathType Leaf) -or [IO.Path]::GetExtension($cli) -ine '.dll')) {
    throw 'SimulatorCliPath must refer to a published simulator CLI DLL.'
}
function Invoke-Simulator([string[]]$Options) {
    if ($cli) { & dotnet $cli @Options }
    else { & dotnet run -c Release --project $project -- @Options }
    if ($LASTEXITCODE -ne 0) { throw "Simulator '$($Options[0])' returned $LASTEXITCODE. Retain its diagnostics and run bundle." }
}
Push-Location $root
try {
    if ($observations) { Invoke-Simulator @('calibrate','--observations',$observations,'--output',$calibration,'--mqtty-repository',$root) }
    & (Join-Path $PSScriptRoot 'NewSimulationConfig.ps1') -CalibrationPath $calibration -OutputPath $config `
        -Broker $Broker -MessageBytes $MessageBytes -Qos $Qos -Publishers $Publishers -Seed $Seed `
        -MqttYRepository $root -MqttBenchmarkRepository $benchmark
    Invoke-Simulator @('experiment','--config',$config,'--output',$output)
} finally { Pop-Location }
