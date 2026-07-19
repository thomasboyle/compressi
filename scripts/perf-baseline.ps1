# Multi-run launch / memory / idle-CPU baseline for Compressi (Release unpackaged).
param(
    [string]$ExePath = "",
    [int]$Runs = 5,
    [int]$IdleSeconds = 15,
    [string]$OutDir = ""
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $ExePath) {
    # Prefer WindowsAppSDKSelfContained publish (unpackaged framework build exits REGDB_E_CLASSNOTREG).
    $candidates = @(
        (Join-Path $Root "Compressi.App\bin\Release\net8.0-windows10.0.26100.0\win-x64\setup-publish\Compressi.App.exe"),
        (Join-Path $Root "Compressi.App\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\Compressi.App.exe"),
        (Join-Path $Root "Compressi.App\bin\Release\net8.0-windows10.0.26100.0\win-x64\Compressi.App.exe")
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw "Compressi.App.exe not found. Build Release first."
}
if (-not $OutDir) {
    $OutDir = Join-Path $env:TEMP "compressi-perf"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$env:COMPRESSI_PERF = '1'
$launchRows = @()

function Get-Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $sorted[[int]([math]::Floor(($sorted.Count - 1) / 2))]
}

function Wait-MainWindow([System.Diagnostics.Process]$proc, [int]$timeoutMs = 60000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $proc.Refresh()
        if ($proc.HasExited) { return $false }
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

Write-Host "Exe: $ExePath"
Write-Host "Runs: $Runs  IdleSeconds: $IdleSeconds"
Write-Host "OutDir: $OutDir"

for ($i = 1; $i -le $Runs; $i++) {
    Write-Host "`n=== Launch run $i / $Runs ==="
    Get-Process Compressi.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $ExePath -PassThru -WorkingDirectory (Split-Path $ExePath)
    $visible = Wait-MainWindow $proc
    $sw.Stop()
    $timeToWindowMs = $sw.Elapsed.TotalMilliseconds

    # Allow deferred page precreate + first paint settle for TTI proxy
    Start-Sleep -Milliseconds 1500
    $proc.Refresh()
    $wsAfterLaunch = [math]::Round($proc.WorkingSet64 / 1MB, 2)
    $privateAfterLaunch = [math]::Round($proc.PrivateMemorySize64 / 1MB, 2)

    # Idle CPU sample (~IdleSeconds)
    $cpuSamples = @()
    $idleSw = [Diagnostics.Stopwatch]::StartNew()
    $prevCpu = $proc.TotalProcessorTime
    $prevWall = [Diagnostics.Stopwatch]::StartNew()
    while ($idleSw.Elapsed.TotalSeconds -lt $IdleSeconds) {
        Start-Sleep -Milliseconds 1000
        $proc.Refresh()
        if ($proc.HasExited) { break }
        $cpuDelta = ($proc.TotalProcessorTime - $prevCpu).TotalMilliseconds
        $wallDelta = $prevWall.Elapsed.TotalMilliseconds
        $prevCpu = $proc.TotalProcessorTime
        $prevWall.Restart()
        $cores = [Environment]::ProcessorCount
        $pct = if ($wallDelta -gt 0) { ($cpuDelta / $wallDelta) * 100.0 / $cores } else { 0 }
        $cpuSamples += [math]::Round($pct, 3)
    }

    $proc.Refresh()
    $wsAfterIdle = [math]::Round($proc.WorkingSet64 / 1MB, 2)

    # Navigation responsiveness proxy: bring window forward (external); detailed UI marks come from PerfProbe
    $perfLog = Join-Path $env:TEMP "compressi-perf\run-$($proc.Id).jsonl"
    $ttiMs = $null
    $marks = @{}
    if (Test-Path $perfLog) {
        Get-Content $perfLog | ForEach-Object {
            try {
                $j = $_ | ConvertFrom-Json
                $marks[$j.name] = $j
                if ($j.name -eq 'tti') { $ttiMs = [double]$j.t_ms }
            } catch {}
        }
    }

    $row = [pscustomobject]@{
        run = $i
        pid = $proc.Id
        window_visible = $visible
        time_to_window_ms = [math]::Round($timeToWindowMs, 1)
        tti_ms = $(if ($null -ne $ttiMs) { [math]::Round($ttiMs, 1) } else { $null })
        ws_launch_mb = $wsAfterLaunch
        private_launch_mb = $privateAfterLaunch
        ws_idle_mb = $wsAfterIdle
        idle_cpu_median_pct = $(if ($cpuSamples.Count) { [math]::Round((Get-Median $cpuSamples), 3) } else { $null })
        idle_cpu_mean_pct = $(if ($cpuSamples.Count) { [math]::Round(($cpuSamples | Measure-Object -Average).Average, 3) } else { $null })
        perf_log = $(if (Test-Path $perfLog) { $perfLog } else { $null })
        marks = ($marks.Keys -join ',')
    }
    $launchRows += $row
    $row | Format-List | Out-String | Write-Host

    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Warm starts = runs 2..N (run 1 treated as colder for this session)
$windowTimes = @($launchRows | ForEach-Object { $_.time_to_window_ms })
$ttiTimes = @($launchRows | Where-Object { $null -ne $_.tti_ms } | ForEach-Object { $_.tti_ms })
$wsLaunch = @($launchRows | ForEach-Object { $_.ws_launch_mb })
$wsIdle = @($launchRows | ForEach-Object { $_.ws_idle_mb })
$idleCpu = @($launchRows | Where-Object { $null -ne $_.idle_cpu_median_pct } | ForEach-Object { $_.idle_cpu_median_pct })

function Summarize([double[]]$values) {
    if (-not $values -or $values.Count -eq 0) { return $null }
    $mean = ($values | Measure-Object -Average).Average
    $var = ($values | ForEach-Object { ($_ - $mean) * ($_ - $mean) } | Measure-Object -Average).Average
    [pscustomobject]@{
        n = $values.Count
        median = [math]::Round((Get-Median $values), 2)
        mean = [math]::Round($mean, 2)
        stdev = [math]::Round([math]::Sqrt($var), 2)
        min = [math]::Round(($values | Measure-Object -Minimum).Minimum, 2)
        max = [math]::Round(($values | Measure-Object -Maximum).Maximum, 2)
    }
}

$summary = [pscustomobject]@{
    createdAt = (Get-Date).ToString('o')
    exe = $ExePath
    runs = $Runs
    time_to_window_ms = Summarize $windowTimes
    tti_ms = Summarize $ttiTimes
    cold_time_to_window_ms = $launchRows[0].time_to_window_ms
    warm_time_to_window_ms = Summarize @($windowTimes | Select-Object -Skip 1)
    ws_launch_mb = Summarize $wsLaunch
    ws_idle_mb = Summarize $wsIdle
    idle_cpu_median_pct = Summarize $idleCpu
    runs_detail = $launchRows
}

$reportPath = Join-Path $OutDir ("launch-baseline-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "`nWrote $reportPath"
$summary | ConvertTo-Json -Depth 4
