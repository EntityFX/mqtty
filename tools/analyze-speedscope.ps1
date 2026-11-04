# analyze-speedscope.ps1 — агрегация self-time по функциям из SpeedScope JSON (конверт dotnet-trace)
# Idle-фреймы (ожидание ThreadPool/сигналов) исключаются из расчёта, чтобы показать CPU-горячие точки.
# Использование: powershell -File tools\analyze-speedscope.ps1 -File lb-current.speedscope.speedscope.json -Top 25
param(
    [Parameter(Mandatory=$true)][string]$File,
    [int]$Top = 25,
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$data = Get-Content $File -Raw | ConvertFrom-Json

$selfTime = @{}
$cpuTime = 0.0
$frameName = $data.shared.frames

# Idle/шумовые фреймы: ожидания пула, инфраструктура трассировки
$idleRegex = '^(UNMANAGED_CODE_TIME|\?\!\?|Process64 |Threads|\(Non-Activities\)|Thread \()|' +
    'LowLevelLifoSemaphore|WaitHandle\.WaitOneNoCheck|WorkerThreadStart|GateThreadStart|' +
    'Thread\.StartCallback|Thread\.Sleep|Monitor\.Wait\(|ManualResetEventSlim\.Wait\(|' +
    'NativeRuntimeEventSource|SimpleUnixNativeTimer'

foreach ($profile in $data.profiles) {
    if ($profile.type -ne "evented" -and $profile.type -ne "sampled") { continue }
    $unit = $profile.unit
    $stack = [System.Collections.Generic.List[int]]::new()
    $openAt = [System.Collections.Generic.List[double]]::new()
    foreach ($ev in $profile.events) {
        if ($ev.type -eq "O") {
            $stack.Add($ev.frame)
            $openAt.Add($ev.at)
        }
        elseif ($ev.type -eq "C") {
            $idx = $stack.LastIndexOf([int]$ev.frame)
            if ($idx -ge 0) {
                $isLeaf = ($idx -eq $stack.Count - 1)
                $duration = $ev.at - $openAt[$idx]
                if ($isLeaf) {
                    $name = $frameName[$ev.frame].name
                    if ($name -notmatch $idleRegex) {
                        if (-not $selfTime.ContainsKey($name)) { $selfTime[$name] = 0.0 }
                        $selfTime[$name] += $duration
                        $cpuTime += $duration
                    }
                }
                $stack.RemoveAt($idx)
                $openAt.RemoveAt($idx)
            }
        }
    }
}

Write-Host ("CPU self-time (без idle-ожиданий): {0:N0} {1}" -f $cpuTime, $profile.unit)
Write-Host ""
$sorted = $selfTime.GetEnumerator() | Sort-Object Value -Descending
$rank = 0
foreach ($item in $sorted) {
    $rank++
    if ($rank -gt $Top) { break }
    if ($Filter -ne "" -and $item.Key -notmatch $Filter) { continue }
    $pct = 100.0 * $item.Value / $cpuTime
    Write-Host ("{0,3}. {1,7:P2}  {2,10:N0} {3}  {4}" -f $rank, $pct, [Math]::Round($item.Value), $profile.unit, $item.Key)
}
