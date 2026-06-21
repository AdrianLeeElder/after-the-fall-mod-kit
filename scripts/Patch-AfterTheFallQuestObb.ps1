param(
    [string]$SourceObb = (Join-Path (Split-Path -Parent $PSScriptRoot) 'quest-apk\main.38148.com.vertigogames.atf.obb'),
    [string]$OutputObb = (Join-Path (Split-Path -Parent $PSScriptRoot) 'quest-apk\patched\obb\main.38148.com.vertigogames.atf.bloodless.obb'),
    [string]$AdbSerial,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$BytePreservingEncoding = [System.Text.Encoding]::GetEncoding(28591)

$QuestPackageName = 'com.vertigogames.atf'
$QuestObbName = 'main.38148.com.vertigogames.atf.obb'
$QuestObbDirectory = "/sdcard/Android/obb/$QuestPackageName"
$QuestObbPath = "$QuestObbDirectory/$QuestObbName"
$QuestBackupDirectory = '/sdcard/Download/AfterTheFallVRModKit/obb-backup'

$BloodSettingsEntries = @(
    'assets/bin/Data/541ba57ea63899e478c25da546f15ed9',
    'assets/bin/Data/eccc90d64e804de4ba7eb24708909a7b'
)

$ZombieDeathSettingsEntries = @(
    'assets/bin/Data/34371bffbaebc5b43be10f0d2ca3d2f0',
    'assets/bin/Data/5f8e8990ebe7b194c9b3f71f884e565d'
)

$ZombieSkinCollectionEntries = @(
    'assets/bin/Data/20d7b4b282b28ea43aa03ab4dddd00f0',
    'assets/bin/Data/998e200d03c67f34b90e8ca5b36a7991'
)

$ImpactSettingsEntries = @(
    'assets/bin/Data/09772e8d18fb0ba4c922b384b54826a7',
    'assets/bin/Data/2f43c97460f90344e890c1ab531d42eb',
    'assets/bin/Data/30f12ef5552b8d04f8d84053f3a2d6e8',
    'assets/bin/Data/4e360a2e0d6cc4343bfb0d8e576594cc',
    'assets/bin/Data/5c23bb0e163aae14da39a0111525dc74',
    'assets/bin/Data/809fa95a6f185484d9bca2ba75980bde',
    'assets/bin/Data/84c9c1bf03bf08a42bc2622234e637cc',
    'assets/bin/Data/ba2389a2b6d8c384fa8ac8843505ddd0',
    'assets/bin/Data/deaeba1cfa1a0274fb76a3e9ce834770',
    'assets/bin/Data/e5554a9465c02da498404024ec8fb02c',
    'assets/bin/Data/eb4d35904fc40c14095316abef7f47d9',
    'assets/bin/Data/eb60c4d26e4485b48b7e8c5a368ef995',
    'assets/bin/Data/20d7b4b282b28ea43aa03ab4dddd00f0',
    'assets/bin/Data/998e200d03c67f34b90e8ca5b36a7991'
)

$BloodTextureArrayFields = @(
    'bloodPoolTextures',
    'straightDecalTextures',
    'angledDecalTextures',
    'indirectSplatterDecalTextures',
    'zombieBloodDecalTextures',
    'gibFloorBloodTextures',
    'gibSplatterBloodTextures'
)

$ReportRows = New-Object System.Collections.Generic.List[object]

function New-PaddedValue {
    param(
        [string]$OldValue,
        [string]$NewValue
    )

    if ($NewValue.Length -gt $OldValue.Length) {
        throw "Replacement '$NewValue' is longer than original '$OldValue'."
    }

    return $NewValue + (' ' * ($OldValue.Length - $NewValue.Length))
}

function Add-ReportRow {
    param(
        [string]$EntryName,
        [string]$FieldName,
        [int]$Count
    )

    $ReportRows.Add([pscustomobject]@{
        Entry = $EntryName
        Field = $FieldName
        Count = $Count
    }) | Out-Null
}

function Set-FieldNumber {
    param(
        [string]$Text,
        [string]$EntryName,
        [string]$FieldName,
        [string]$NewValue
    )

    $script:__fieldReplaceCount = 0
    $pattern = '("' + [regex]::Escape($FieldName) + '"\s*:\s*)(-?\d+(?:\.\d+)?)'
    $result = [regex]::Replace($Text, $pattern, {
        param($match)

        [void]($script:__fieldReplaceCount = $script:__fieldReplaceCount + 1)
        $oldValue = $match.Groups[2].Value
        return ($match.Groups[1].Value + (New-PaddedValue -OldValue $oldValue -NewValue $NewValue))
    })

    $count = $script:__fieldReplaceCount
    $script:__fieldReplaceCount = 0
    Add-ReportRow -EntryName $EntryName -FieldName $FieldName -Count $count
    return $result
}

function Set-ScopedNumber {
    param(
        [string]$Text,
        [string]$EntryName,
        [string]$ScopeFieldName,
        [string]$ValueFieldName,
        [string]$NewValue,
        [int]$Window = 700
    )

    $scopeNeedle = '"' + $ScopeFieldName + '"'
    $pattern = '("' + [regex]::Escape($ValueFieldName) + '"\s*:\s*)(-?\d+(?:\.\d+)?)'
    $position = 0
    $count = 0

    while ($true) {
        $scopeIndex = $Text.IndexOf($scopeNeedle, $position, [StringComparison]::Ordinal)
        if ($scopeIndex -lt 0) {
            break
        }

        $segmentLength = [Math]::Min($Window, $Text.Length - $scopeIndex)
        $segment = $Text.Substring($scopeIndex, $segmentLength)
        $match = [regex]::Match($segment, $pattern)
        if (!$match.Success) {
            throw "Could not find '$ValueFieldName' inside '$ScopeFieldName' in $EntryName."
        }

        $oldValue = $match.Groups[2].Value
        $newPaddedValue = New-PaddedValue -OldValue $oldValue -NewValue $NewValue
        $absoluteValueIndex = $scopeIndex + $match.Groups[2].Index
        $Text = $Text.Remove($absoluteValueIndex, $oldValue.Length).Insert($absoluteValueIndex, $newPaddedValue)
        $position = $scopeIndex + $scopeNeedle.Length
        $count++
    }

    Add-ReportRow -EntryName $EntryName -FieldName "$ScopeFieldName.$ValueFieldName" -Count $count
    return $Text
}

function Set-ScopedNumberIfPresent {
    param(
        [string]$Text,
        [string]$EntryName,
        [string]$ScopeFieldName,
        [string]$ValueFieldName,
        [string]$NewValue,
        [int]$Window = 900
    )

    $scopeNeedle = '"' + $ScopeFieldName + '"'
    $pattern = '("' + [regex]::Escape($ValueFieldName) + '"\s*:\s*)(-?\d+(?:\.\d+)?)'
    $position = 0
    $count = 0

    while ($true) {
        $scopeIndex = $Text.IndexOf($scopeNeedle, $position, [StringComparison]::Ordinal)
        if ($scopeIndex -lt 0) {
            break
        }

        $segmentLength = [Math]::Min($Window, $Text.Length - $scopeIndex)
        $segment = $Text.Substring($scopeIndex, $segmentLength)
        $match = [regex]::Match($segment, $pattern)
        if ($match.Success) {
            $oldValue = $match.Groups[2].Value
            $newPaddedValue = New-PaddedValue -OldValue $oldValue -NewValue $NewValue
            $absoluteValueIndex = $scopeIndex + $match.Groups[2].Index
            $Text = $Text.Remove($absoluteValueIndex, $oldValue.Length).Insert($absoluteValueIndex, $newPaddedValue)
            $count++
        }

        $position = $scopeIndex + $scopeNeedle.Length
    }

    Add-ReportRow -EntryName $EntryName -FieldName "$ScopeFieldName.$ValueFieldName" -Count $count
    return $Text
}

function Get-JsonValueStart {
    param(
        [string]$Text,
        [int]$ColonIndex
    )

    $index = $ColonIndex + 1
    while ($index -lt $Text.Length -and [char]::IsWhiteSpace($Text[$index])) {
        $index++
    }

    if ($index -ge $Text.Length) {
        throw "Could not find value after JSON field."
    }

    return $index
}

function Get-MatchingJsonDelimiter {
    param(
        [string]$Text,
        [int]$OpenIndex,
        [char]$OpenChar,
        [char]$CloseChar
    )

    $depth = 0
    $inString = $false
    $escaped = $false

    for ($index = $OpenIndex; $index -lt $Text.Length; $index++) {
        $char = $Text[$index]

        if ($inString) {
            if ($escaped) {
                $escaped = $false
            } elseif ($char -eq '\') {
                $escaped = $true
            } elseif ($char -eq '"') {
                $inString = $false
            }

            continue
        }

        if ($char -eq '"') {
            $inString = $true
        } elseif ($char -eq $OpenChar) {
            $depth++
        } elseif ($char -eq $CloseChar) {
            $depth--
            if ($depth -eq 0) {
                return $index
            }
        }
    }

    throw "Could not find matching JSON delimiter '$CloseChar'."
}

function Set-PaddedRange {
    param(
        [string]$Text,
        [int]$Start,
        [int]$Length,
        [string]$NewValue
    )

    if ($NewValue.Length -gt $Length) {
        throw "Replacement '$NewValue' is longer than original range length $Length."
    }

    $paddedValue = $NewValue + (' ' * ($Length - $NewValue.Length))
    return $Text.Remove($Start, $Length).Insert($Start, $paddedValue)
}

function Clear-JsonArrayAt {
    param(
        [string]$Text,
        [int]$ArrayStart
    )

    $arrayEnd = Get-MatchingJsonDelimiter -Text $Text -OpenIndex $ArrayStart -OpenChar '[' -CloseChar ']'
    $arrayLength = $arrayEnd - $ArrayStart + 1
    return Set-PaddedRange -Text $Text -Start $ArrayStart -Length $arrayLength -NewValue '[]'
}

function Clear-ArrayField {
    param(
        [string]$Text,
        [string]$EntryName,
        [string]$FieldName
    )

    $fieldNeedle = '"' + $FieldName + '"'
    $position = 0
    $count = 0

    while ($true) {
        $fieldIndex = $Text.IndexOf($fieldNeedle, $position, [StringComparison]::Ordinal)
        if ($fieldIndex -lt 0) {
            break
        }

        $colonIndex = $Text.IndexOf(':', $fieldIndex + $fieldNeedle.Length)
        if ($colonIndex -lt 0) {
            throw "Could not find ':' after '$FieldName' in $EntryName."
        }

        $valueStart = Get-JsonValueStart -Text $Text -ColonIndex $colonIndex
        if ($Text[$valueStart] -eq '[') {
            $Text = Clear-JsonArrayAt -Text $Text -ArrayStart $valueStart
            $count++
        } elseif ($Text[$valueStart] -eq '{') {
            $objectEnd = Get-MatchingJsonDelimiter -Text $Text -OpenIndex $valueStart -OpenChar '{' -CloseChar '}'
            $objectLength = $objectEnd - $valueStart + 1
            $objectText = $Text.Substring($valueStart, $objectLength)

            $countMatch = [regex]::Match($objectText, '("\$count"\s*:\s*)(\d+)')
            if ($countMatch.Success) {
                $oldCount = $countMatch.Groups[2].Value
                $absoluteCountIndex = $valueStart + $countMatch.Groups[2].Index
                $Text = $Text.Remove($absoluteCountIndex, $oldCount.Length).Insert($absoluteCountIndex, (New-PaddedValue -OldValue $oldCount -NewValue '0'))
            }

            $valueNeedle = '"$value"'
            $valueFieldIndex = $Text.IndexOf($valueNeedle, $valueStart, $objectLength, [StringComparison]::Ordinal)
            if ($valueFieldIndex -lt 0) {
                throw "Could not find '$valueNeedle' inside '$FieldName' in $EntryName."
            }

            $valueColonIndex = $Text.IndexOf(':', $valueFieldIndex + $valueNeedle.Length)
            $arrayStart = Get-JsonValueStart -Text $Text -ColonIndex $valueColonIndex
            if ($Text[$arrayStart] -ne '[') {
                throw "Expected '$FieldName.$valueNeedle' to be an array in $EntryName."
            }

            $Text = Clear-JsonArrayAt -Text $Text -ArrayStart $arrayStart
            $count++
        } else {
            throw "Expected '$FieldName' to be an array or typed array object in $EntryName."
        }

        $position = $fieldIndex + $fieldNeedle.Length
    }

    Add-ReportRow -EntryName $EntryName -FieldName "$FieldName.emptyArray" -Count $count
    return $Text
}

function Set-BloodSettings {
    param(
        [string]$Text,
        [string]$EntryName
    )

    foreach ($field in @(
        'bulletMinDecalSize',
        'bulletMaxDecalSize',
        'bulletMinSizeDistance',
        'bulletMaxDistance',
        'indirectMinDecalSize',
        'indirectMaxDecalSize',
        'gibFloorPaintDelay',
        'maxGibSplatterRaycastDistance',
        'maxGibSplatterRandomVerticalAngle',
        'maxGibSplatterRandomHirozntalAngleFraction'
    )) {
        $Text = Set-FieldNumber -Text $Text -EntryName $EntryName -FieldName $field -NewValue '0.0'
    }

    foreach ($scope in @('minMaxGibFloorBloodSize', 'gibSplatterPaintDelay', 'minMaxGibSplatterBloodSize')) {
        $Text = Set-ScopedNumber -Text $Text -EntryName $EntryName -ScopeFieldName $scope -ValueFieldName 'x' -NewValue '0.0'
        $Text = Set-ScopedNumber -Text $Text -EntryName $EntryName -ScopeFieldName $scope -ValueFieldName 'y' -NewValue '0.0'
    }

    $Text = Set-ScopedNumber -Text $Text -EntryName $EntryName -ScopeFieldName 'gibSplatterRaycastMask' -ValueFieldName 'value' -NewValue '0'
    foreach ($field in $BloodTextureArrayFields) {
        $Text = Clear-ArrayField -Text $Text -EntryName $EntryName -FieldName $field
    }

    return $Text
}

function Set-ZombieDeathSettings {
    param(
        [string]$Text,
        [string]$EntryName
    )

    foreach ($field in @(
        'bloodPoolMinSize',
        'bloodPoolMaxSize',
        'bloodPoolMinSpawnDuration',
        'bloodPoolMaxSpawnDuration'
    )) {
        $Text = Set-FieldNumber -Text $Text -EntryName $EntryName -FieldName $field -NewValue '0.0'
    }

    return $Text
}

function Set-ImpactSettings {
    param(
        [string]$Text,
        [string]$EntryName
    )

    foreach ($field in @(
        'mutilationType',
        'MutilationType',
        'impactType',
        'ImpactType',
        'gibbingSettings',
        'GibbingSettings'
    )) {
        $Text = Set-FieldNumber -Text $Text -EntryName $EntryName -FieldName $field -NewValue '0'
    }

    foreach ($field in @(
        'criticalHitChance',
        'CriticalHitChance'
    )) {
        $Text = Set-FieldNumber -Text $Text -EntryName $EntryName -FieldName $field -NewValue '0.0'
    }

    if ($ZombieSkinCollectionEntries -contains $EntryName) {
        $Text = Set-ZombieSkinCollection -Text $Text -EntryName $EntryName
    }

    return $Text
}

function Set-ZombieSkinCollection {
    param(
        [string]$Text,
        [string]$EntryName
    )

    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'r' -NewValue '0.6'
    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'g' -NewValue '0.8'
    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'b' -NewValue '1.0'
    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'x' -NewValue '0.6'
    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'y' -NewValue '0.8'
    $Text = Set-ScopedNumberIfPresent -Text $Text -EntryName $EntryName -ScopeFieldName 'colorMultiplier' -ValueFieldName 'z' -NewValue '1.0'
    return $Text
}

function Update-ObbEntry {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$EntryName,
        [scriptblock]$Patch
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "OBB is missing $EntryName."
    }

    $lastWriteTime = $entry.LastWriteTime
    $originalLength = $entry.Length
    $stream = $entry.Open()
    try {
        $memory = New-Object System.IO.MemoryStream
        $stream.CopyTo($memory)
        $originalBytes = $memory.ToArray()
    }
    finally {
        $stream.Dispose()
    }

    $text = $BytePreservingEncoding.GetString($originalBytes)
    $patchedText = & $Patch $text $EntryName
    $patchedBytes = $BytePreservingEncoding.GetBytes($patchedText)

    if ($patchedBytes.Length -ne $originalBytes.Length) {
        throw "Patched length changed for $EntryName`: $($originalBytes.Length) -> $($patchedBytes.Length)."
    }

    if ($patchedBytes.Length -ne $originalLength) {
        throw "Unexpected length mismatch for $EntryName."
    }

    $entry.Delete()
    $newEntry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $newEntry.LastWriteTime = $lastWriteTime
    $outStream = $newEntry.Open()
    try {
        $outStream.Write($patchedBytes, 0, $patchedBytes.Length)
    }
    finally {
        $outStream.Dispose()
    }
}

function Invoke-Adb {
    param([string[]]$Arguments)

    $adb = Get-Command adb.exe -ErrorAction Stop
    $fullArgs = New-Object System.Collections.Generic.List[string]
    if ($AdbSerial) {
        $fullArgs.Add('-s') | Out-Null
        $fullArgs.Add($AdbSerial) | Out-Null
    }
    foreach ($argument in $Arguments) {
        $fullArgs.Add($argument) | Out-Null
    }

    & $adb.Source @($fullArgs.ToArray())
    if ($LASTEXITCODE -ne 0) {
        throw "adb $($fullArgs -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$resolvedSourceObb = (Resolve-Path -LiteralPath $SourceObb).ProviderPath
$resolvedOutputObb = $OutputObb
$outputDirectory = Split-Path -Parent $resolvedOutputObb
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

if ((Test-Path -LiteralPath $resolvedOutputObb) -and ((Resolve-Path -LiteralPath $resolvedOutputObb).ProviderPath -ne $resolvedSourceObb)) {
    Remove-Item -LiteralPath $resolvedOutputObb -Force
}

if ((Test-Path -LiteralPath $resolvedOutputObb) -and ((Resolve-Path -LiteralPath $resolvedOutputObb).ProviderPath -eq $resolvedSourceObb)) {
    throw "OutputObb must be different from SourceObb."
}

Copy-Item -LiteralPath $resolvedSourceObb -Destination $resolvedOutputObb -Force

$archive = [System.IO.Compression.ZipFile]::Open($resolvedOutputObb, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($entryName in $BloodSettingsEntries) {
        Update-ObbEntry -Archive $archive -EntryName $entryName -Patch {
            param($text, $entryName)
            Set-BloodSettings -Text $text -EntryName $entryName
        }
    }

    foreach ($entryName in $ZombieDeathSettingsEntries) {
        Update-ObbEntry -Archive $archive -EntryName $entryName -Patch {
            param($text, $entryName)
            Set-ZombieDeathSettings -Text $text -EntryName $entryName
        }
    }

    foreach ($entryName in $ImpactSettingsEntries) {
        Update-ObbEntry -Archive $archive -EntryName $entryName -Patch {
            param($text, $entryName)
            Set-ImpactSettings -Text $text -EntryName $entryName
        }
    }
}
finally {
    $archive.Dispose()
}

$reportPath = [System.IO.Path]::ChangeExtension($resolvedOutputObb, '.patch-report.csv')
$ReportRows | Export-Csv -NoTypeInformation -Path $reportPath

Write-Host "Patched OBB: $resolvedOutputObb"
Write-Host "Patch report: $reportPath"

if ($Install) {
    Write-Host "Stopping Quest game package..."
    Invoke-Adb -Arguments @('shell', 'am', 'force-stop', $QuestPackageName)

    Write-Host "Creating remote OBB backup..."
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $remoteBackupPath = "$QuestBackupDirectory/$QuestObbName.$stamp.bak"
    Invoke-Adb -Arguments @('shell', 'mkdir', '-p', $QuestBackupDirectory)
    Invoke-Adb -Arguments @('shell', 'cp', $QuestObbPath, $remoteBackupPath)

    Write-Host "Pushing patched OBB..."
    Invoke-Adb -Arguments @('push', $resolvedOutputObb, $QuestObbPath)
    Write-Host "Installed patched OBB. Backup: $remoteBackupPath"
}
