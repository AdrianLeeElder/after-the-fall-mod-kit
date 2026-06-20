param(
    [string]$LogPath = "$env:USERPROFILE\AppData\LocalLow\Vertigo Games\AfterTheFall\Player.log"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath)) {
    throw "Log not found: $LogPath"
}

$lines = Get-Content -LiteralPath $LogPath
$timestampPattern = '^\[(\d{2}:\d{2}:\d{2}\.\d{3})\]'
$timestampMatches = Select-String -Path $LogPath -Pattern $timestampPattern

Write-Host "Log: $LogPath"
Write-Host "Size: $([math]::Round((Get-Item -LiteralPath $LogPath).Length / 1MB, 2)) MB"

if ($timestampMatches.Count -gt 0) {
    $first = [regex]::Match($timestampMatches[0].Line, $timestampPattern).Groups[1].Value
    $last = [regex]::Match($timestampMatches[-1].Line, $timestampPattern).Groups[1].Value
    Write-Host "Timestamp span: $first -> $last"
}

Write-Host ""
Write-Host "Top logger categories:"
Select-String -Path $LogPath -Pattern '^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[(?<level>[^\]]+)\] \[(?<cat>[^\]]+)\]' |
    ForEach-Object { $_.Matches[0].Groups['cat'].Value } |
    Group-Object |
    Sort-Object Count -Descending |
    Select-Object -First 20 Count,Name |
    Format-Table -AutoSize

Write-Host ""
Write-Host "Warnings/errors by level:"
Select-String -Path $LogPath -Pattern '^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[(?<level>[^\]]+)\]' |
    ForEach-Object { $_.Matches[0].Groups['level'].Value } |
    Group-Object |
    Sort-Object Count -Descending |
    Format-Table -AutoSize

Write-Host ""
Write-Host "ComputeBuffer GC warnings:"
$computeWarnings = Select-String -Path $LogPath -Pattern 'GarbageCollector disposing of ComputeBuffer'
Write-Host "Count: $($computeWarnings.Count)"
foreach ($warning in $computeWarnings) {
    $nearestTimestamp = ""
    for ($i = $warning.LineNumber - 1; $i -ge 0 -and $i -ge ($warning.LineNumber - 201); $i--) {
        $m = [regex]::Match($lines[$i], '\[(\d{2}:\d{2}:\d{2}\.\d{3})\]')
        if ($m.Success) {
            $nearestTimestamp = $m.Groups[1].Value
            break
        }
    }
    "{0}: nearest timestamp {1}" -f $warning.LineNumber, $nearestTimestamp
}

Write-Host ""
Write-Host "Asset unload / GC pauses:"
Select-String -Path $LogPath -Pattern 'Unloading .*unused Assets|Total: .* ms|UnloadTime: .* ms' |
    ForEach-Object { "{0}: {1}" -f $_.LineNumber, $_.Line }

Write-Host ""
Write-Host "Scene and horde transitions:"
Select-String -Path $LogPath -Pattern 'Loading Next Scene|GameMode changed|HM\d+_|Horde' |
    Select-Object -Last 80 LineNumber,Line |
    Format-Table -AutoSize
