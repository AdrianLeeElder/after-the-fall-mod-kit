param(
    [string]$Configuration = 'Release',
    [string]$EmbeddedPayloadZip
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'AfterTheFallVRModKitManager.cs'
$outDir = Join-Path $root "bin\$Configuration"
$out = Join-Path $outDir 'AfterTheFallVRModKitManager.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Could not find .NET Framework compiler: $csc"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$resourceArgs = @()
if ($EmbeddedPayloadZip) {
    if (-not (Test-Path -LiteralPath $EmbeddedPayloadZip)) {
        throw "Embedded payload zip not found: $EmbeddedPayloadZip"
    }

    $resourceArgs += "/resource:$EmbeddedPayloadZip,AfterTheFallVRModKit.Payload.zip"
}

& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /out:$out `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.Windows.Forms.dll `
    $resourceArgs `
    $src

if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

Write-Host "Built $out"
