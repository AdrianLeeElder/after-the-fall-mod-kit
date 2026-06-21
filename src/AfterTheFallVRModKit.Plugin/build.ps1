param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\After The Fall'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'AfterTheFallVRModKitPlugin.cs'
$outDir = Join-Path $root 'bin'
$out = Join-Path $outDir 'AfterTheFallVRModKit.dll'
$core = Join-Path $GameRoot 'BepInEx\core'
$dotnet = Join-Path $GameRoot 'dotnet'
$csc = 'C:\Program Files\dotnet\sdk\3.1.101\Roslyn\bincore\csc.dll'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path -LiteralPath $csc)) {
    $sdk = & dotnet --list-sdks |
        ForEach-Object {
            if ($_ -match '^(?<version>\S+)\s+\[(?<root>.+)\]') {
                [pscustomobject]@{
                    Version = [version]($matches.version -replace '-.*$', '')
                    Path = Join-Path $matches.root $matches.version
                }
            }
        } |
        Sort-Object Version -Descending |
        Select-Object -First 1

    if ($sdk) {
        $csc = Join-Path $sdk.Path 'Roslyn\bincore\csc.dll'
    }
}

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Could not find Roslyn compiler csc.dll. Install a .NET SDK or update build.ps1."
}

$refs = @(
    (Join-Path $dotnet 'System.Private.CoreLib.dll'),
    (Join-Path $dotnet 'System.Runtime.dll'),
    (Join-Path $dotnet 'System.Console.dll'),
    (Join-Path $dotnet 'System.Collections.dll'),
    (Join-Path $dotnet 'System.Linq.dll'),
    (Join-Path $dotnet 'System.Reflection.dll'),
    (Join-Path $dotnet 'System.Reflection.Extensions.dll'),
    (Join-Path $dotnet 'System.Runtime.Extensions.dll'),
    (Join-Path $dotnet 'netstandard.dll'),
    (Join-Path $core 'BepInEx.Core.dll'),
    (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'),
    (Join-Path $core '0Harmony.dll')
)

foreach ($ref in $refs) {
    if (-not (Test-Path -LiteralPath $ref)) {
        throw "Missing reference: $ref"
    }
}

& dotnet $csc `
    -noconfig `
    -nostdlib+ `
    -langversion:8.0 `
    -target:library `
    -optimize+ `
    -nullable:enable `
    -out:$out `
    ($refs | ForEach-Object { "-r:$_" }) `
    $src

if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

Write-Host "Built $out"
