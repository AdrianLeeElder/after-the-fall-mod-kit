param(
    [string]$ProcessName = "AfterTheFall",
    [int]$Threshold = 30000,
    [int]$IntervalMilliseconds = 500,
    [int]$MaxSnapshots = 3,
    [int]$CooldownSeconds = 20,
    [int]$DurationMinutes = 10,
    [string]$OutputDirectory = ".\captures"
)

$ErrorActionPreference = "Stop"

if ($IntervalMilliseconds -lt 100) {
    throw "IntervalMilliseconds must be 100 or greater."
}

$snapshotScript = Join-Path $PSScriptRoot "Snapshot-ProcessHandles.ps1"
if (-not (Test-Path -LiteralPath $snapshotScript)) {
    throw "Snapshot script not found: $snapshotScript"
}

$endAt = (Get-Date).AddMinutes($DurationMinutes)
$lastSnapshotAt = [datetime]::MinValue
$snapshots = 0
$highWater = 0

Write-Host "Watching $ProcessName.exe handle spikes."
Write-Host "Threshold: $Threshold handles, interval: $IntervalMilliseconds ms, duration: $DurationMinutes min."
Write-Host "This script should be run elevated so snapshots can duplicate target handles."

while ((Get-Date) -lt $endAt -and $snapshots -lt $MaxSnapshots) {
    $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) {
        Write-Host "$(Get-Date -Format T) waiting for $ProcessName.exe..."
        Start-Sleep -Milliseconds $IntervalMilliseconds
        continue
    }

    $handleCount = $proc.HandleCount
    if ($handleCount -gt $highWater) {
        $highWater = $handleCount
        Write-Host ("{0} new high-water handle count: {1}" -f (Get-Date -Format T), $highWater)
    }

    $cooldownElapsed = ((Get-Date) - $lastSnapshotAt).TotalSeconds -ge $CooldownSeconds
    if ($handleCount -ge $Threshold -and $cooldownElapsed) {
        $snapshots++
        $lastSnapshotAt = Get-Date
        Write-Host ("{0} threshold crossed at {1} handles; taking snapshot {2}/{3}..." -f (Get-Date -Format T), $handleCount, $snapshots, $MaxSnapshots)
        & $snapshotScript -TargetPid $proc.Id -OutputDirectory $OutputDirectory
    }

    Start-Sleep -Milliseconds $IntervalMilliseconds
}

Write-Host "Done. High-water handle count observed: $highWater"
