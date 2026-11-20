<#
.SYNOPSIS
    Validate a SolarTestRange release artifact: bundle folder or bundle zip.

.DESCRIPTION
    Catches the failure modes that make AutoCAD skip a bundle silently:

      1. PackageContents.xml is not pure ASCII (a Chinese character re-saved as
         '?' produces an unclosed attribute, the XML becomes invalid, and
         AutoCAD skips the bundle with no error at all).
      2. PackageContents.xml does not parse.
      3. Required elements / attributes are missing, notably
         LoadOnAutoCADStartup="True".
      4. ModuleName points at a file that is not in the bundle.
      5. The zip uses backslashes as path separators, which breaks extraction
         with Windows Explorer and some other tools.

    Exit code 0 = all checks passed. Non-zero = at least one check failed.
    Every failure is printed with a "[FAIL]" prefix so CI output is greppable.

.PARAMETER Path
    A *.bundle directory, a directory containing one, or a *.bundle.zip file.

.EXAMPLE
    pwsh -File tools/Test-Bundle.ps1 -Path dist/SolarTestRange.bundle.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Fail([string]$message) {
    Write-Host "[FAIL] $message" -ForegroundColor Red
    $script:failures++
}

function Ok([string]$message) {
    Write-Host "[ OK ] $message" -ForegroundColor Green
}

function Check-Manifest([string]$manifestPath) {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        Fail "PackageContents.xml not found at the bundle root: $manifestPath"
        return
    }
    Ok "manifest found: $manifestPath"

    # 1. Pure ASCII check. Any byte >= 0x80 is a regression risk.
    $bytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $bad = @()
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -ge 0x80) {
            # Report a short window around the first offenders.
            if ($bad.Count -lt 8) {
                $start = [Math]::Max(0, $i - 12)
                $len = [Math]::Min(24, $bytes.Length - $start)
                $hex = ($bytes[$start..($start + $len - 1)] | ForEach-Object { $_.ToString('x2') }) -join ' '
                $bad += ("offset 0x{0:x} : {1}" -f $i, $hex)
            }
        }
    }
    if ($bad.Count -gt 0) {
        Fail "PackageContents.xml is not pure ASCII. AutoCAD will skip the bundle silently."
        $bad | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkYellow }
    }
    else {
        Ok "PackageContents.xml is pure ASCII"
    }

    # 3. XML parse. This is what AutoCAD does; if it throws, the bundle is dead.
    $doc = New-Object System.Xml.XmlDocument
    try {
        $doc.Load($manifestPath)
        Ok "PackageContents.xml parses as XML"
    }
    catch {
        Fail "PackageContents.xml does not parse: $($_.Exception.Message)"
        return
    }

    # 4. Required structure.
    $pkg = $doc.DocumentElement
    if ($pkg.LocalName -ne 'ApplicationPackage') {
        Fail "root element is <$($pkg.LocalName)>, expected <ApplicationPackage>"
        return
    }
    foreach ($attr in 'Name', 'AppVersion', 'ProductCode') {
        if (-not $pkg.HasAttribute($attr) -or [string]::IsNullOrWhiteSpace($pkg.GetAttribute($attr))) {
            Fail "ApplicationPackage is missing $attr"
        }
    }
    if ($pkg.GetAttribute('ProductCode') -match '1234567890|ABCD-EF') {
        Fail "ProductCode looks like a placeholder: $($pkg.GetAttribute('ProductCode'))"
    }

    $entries = $doc.SelectNodes('//ComponentEntry')
    if ($entries.Count -eq 0) {
        Fail "no <ComponentEntry> element: AutoCAD has nothing to load"
        return
    }

    foreach ($entry in $entries) {
        $appName = $entry.GetAttribute('AppName')
        $module = $entry.GetAttribute('ModuleName')
        if (-not $module) { Fail "ComponentEntry '$appName' has no ModuleName"; continue }

        $startup = $entry.GetAttribute('LoadOnAutoCADStartup')
        if ($startup -ne 'True') {
            Fail "ComponentEntry '$appName' has LoadOnAutoCADStartup='$startup'; must be 'True' for startup autoload"
        }
        else {
            Ok "ComponentEntry '$appName' loads on AutoCAD startup"
        }

        # 5. ModuleName must resolve inside the bundle.
        $rel = $module -replace '^\./', '' -replace '/', [System.IO.Path]::DirectorySeparatorChar
        $expected = Join-Path (Split-Path -Parent $manifestPath) $rel
        if (Test-Path -LiteralPath $expected) {
            $size = (Get-Item -LiteralPath $expected).Length
            Ok "module present: $rel ($size bytes)"
        }
        else {
            Fail "ModuleName resolves to a missing file: $expected"
        }
    }
}

function Check-Zip([string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $backslash = @($zip.Entries | Where-Object { $_.FullName -like '*\*' })
        if ($backslash.Count -gt 0) {
            Fail "zip uses backslashes as path separators ($($backslash.Count) entries). Windows Explorer extraction will produce a broken layout."
            $backslash | Select-Object -First 5 | ForEach-Object { Write-Host "         $($_.FullName)" -ForegroundColor DarkYellow }
        }
        else {
            Ok "zip uses forward slashes"
        }

        $names = $zip.Entries | ForEach-Object { $_.FullName }
        if ($names -match '\.bundle\.bundle/') {
            Fail "zip contains a nested .bundle.bundle directory"
        }
        if (-not ($names -match '^[^/]+\.bundle/PackageContents\.xml$')) {
            Fail "PackageContents.xml is not at the zip's .bundle root"
        }
        else {
            Ok "PackageContents.xml is at the .bundle root"
        }
    }
    finally {
        $zip.Dispose()
    }
}

Write-Host "== SolarTestRange bundle check ==" -ForegroundColor Cyan
$item = Get-Item -LiteralPath $Path
Write-Host "target: $($item.FullName)"

if ($item.PSIsContainer) {
    $manifests = @(Get-ChildItem -LiteralPath $item.FullName -Filter 'PackageContents.xml' -Recurse -ErrorAction SilentlyContinue)
    if ($manifests.Count -eq 0) {
        Fail "no PackageContents.xml anywhere under $($item.FullName)"
    }
    elseif ($manifests.Count -gt 1) {
        Fail "more than one PackageContents.xml under $($item.FullName) (nested bundle?)"
        $manifests | ForEach-Object { Write-Host "         $($_.FullName)" -ForegroundColor DarkYellow }
    }
    $checkRoot = if ($item.Name -like '*.bundle') { $item } else { $manifests | Select-Object -First 1 | ForEach-Object { $_.Directory } }
    if ($checkRoot) { Check-Manifest (Join-Path $checkRoot.FullName 'PackageContents.xml') }
}
else {
    Check-Zip $item.FullName
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("str-check-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $item.FullName -DestinationPath $tmp -Force
        $manifests = @(Get-ChildItem -LiteralPath $tmp -Filter 'PackageContents.xml' -Recurse)
        if ($manifests.Count -eq 1) {
            Check-Manifest $manifests[0].FullName
        }
        elseif ($manifests.Count -eq 0) {
            Fail "PackageContents.xml missing after extraction"
        }
        else {
            Fail "multiple PackageContents.xml files after extraction: $($manifests.Count)"
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($script:failures -gt 0) {
    Write-Host "RESULT: $($script:failures) check(s) FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "RESULT: all checks passed" -ForegroundColor Green
exit 0
