<#
.SYNOPSIS
    Generates a Zadig preset device file (and a matching zadig.ini) for each USBTMC device dev-term
    can see, so binding them to WinUSB in Zadig needs no hand-typed VID/PID hex values.

.DESCRIPTION
    This does NOT install any driver and does NOT require Administrator rights to run. It only
    writes small text files that Zadig itself reads. Installing/replacing a device's driver is still
    a separate, manual, elevated step you do yourself in the Zadig GUI - see
    docs/design/usbtmc-transport.md's "Setting up WinUSB for a USBTMC device" section for the full
    walkthrough and why that step can't be scripted further (Zadig has no command-line install mode,
    and libwdi - the library Zadig itself is built from - does not currently publish a prebuilt
    wdi-simple.exe that could do it silently).

    Zadig: https://zadig.akeo.ie/  (also linked from https://github.com/pbatard/libwdi)

    By default this discovers devices itself by running dev-term's own
    `--listusbtmcdevices true` against DevTerm.Console. Pass -VendorId/-ProductId directly instead
    if you already know them (e.g. from a previous run, or a device this script's discovery step
    can't open far enough to enumerate).

.PARAMETER VendorId
    One or more 4-hex-digit USB vendor IDs (e.g. "1AB1"), skipping auto-discovery. Must be paired
    positionally with -ProductId.

.PARAMETER ProductId
    One or more 4-hex-digit USB product IDs (e.g. "0588"), paired positionally with -VendorId.

.PARAMETER OutputDirectory
    Where to write zadig.ini and the per-device preset files. Defaults to this script's own
    directory.

.PARAMETER RepoRoot
    Path to the dev-term repo root, used to locate DevTerm.Console for auto-discovery. Defaults to
    two levels up from this script (scripts/usbtmc/..\..).

.EXAMPLE
    ./New-ZadigPreset.ps1
    Auto-discovers attached USBTMC devices via `dotnet run --project src/DevTerm.Console --
    --listusbtmcdevices true` and writes a preset file for each one found.

.EXAMPLE
    ./New-ZadigPreset.ps1 -VendorId 1AB1 -ProductId 0588
    Skips discovery and writes a single preset file for that VID/PID pair.
#>
[CmdletBinding()]
param(
    [string[]] $VendorId,
    [string[]] $ProductId,
    [string] $OutputDirectory = $PSScriptRoot,
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

function Get-DetectedUsbtmcDevices {
    param([string] $RepoRoot)

    $consoleProject = Join-Path $RepoRoot "src\DevTerm.Console"
    if (-not (Test-Path $consoleProject)) {
        throw "Could not find DevTerm.Console at '$consoleProject'. Pass -RepoRoot, or use -VendorId/-ProductId instead of auto-discovery."
    }

    Write-Host "Running 'dotnet run --project $consoleProject -- --listusbtmcdevices true' to discover attached devices..."
    $output = & dotnet run --project $consoleProject -- --listusbtmcdevices true 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run --listusbtmcdevices true failed (exit $LASTEXITCODE):`n$($output -join [Environment]::NewLine)"
    }

    $devices = @()
    foreach ($line in $output) {
        if ($line -match '^([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\s*(.*)$') {
            $devices += [PSCustomObject]@{
                VendorId    = $Matches[1].ToUpperInvariant()
                ProductId   = $Matches[2].ToUpperInvariant()
                Description = $Matches[3].Trim()
            }
        }
    }

    return $devices
}

if ($VendorId -and $ProductId) {
    if ($VendorId.Count -ne $ProductId.Count) {
        throw "-VendorId and -ProductId must have the same number of values (paired positionally)."
    }

    $devices = for ($i = 0; $i -lt $VendorId.Count; $i++) {
        [PSCustomObject]@{
            VendorId    = $VendorId[$i].ToUpperInvariant()
            ProductId   = $ProductId[$i].ToUpperInvariant()
            Description = ""
        }
    }
}
else {
    $devices = Get-DetectedUsbtmcDevices -RepoRoot $RepoRoot
}

if (-not $devices -or $devices.Count -eq 0) {
    Write-Warning "No USBTMC devices found (and none passed via -VendorId/-ProductId). Nothing to write."
    exit 1
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# default_driver: 0 = WinUSB, 1 = libusb0.sys, 2 = libusbK.sys, 3 = Custom (see the Zadig wiki's
# "Zadig" page, [driver] section). list_all = true is required for most USBTMC devices to even show
# up in Zadig's device list, since Windows already has *some* (non-WinUSB) driver bound to them.
$zadigIniPath = Join-Path $OutputDirectory "zadig.ini"
@"
[general]
advanced_mode = true

[device]
list_all = true

[driver]
default_driver = 0
"@ | Set-Content -Path $zadigIniPath -Encoding ASCII

Write-Host "Wrote $zadigIniPath"

foreach ($device in $devices) {
    $description = if ($device.Description) { "USBTMC $($device.Description)" } else { "USBTMC device (dev-term)" }
    $presetPath = Join-Path $OutputDirectory "zadig-preset-$($device.VendorId)-$($device.ProductId).cfg"

    @"
[device]
Description = "$description"
VID = 0x$($device.VendorId)
PID = 0x$($device.ProductId)
"@ | Set-Content -Path $presetPath -Encoding ASCII

    Write-Host "Wrote $presetPath (VID_$($device.VendorId)&PID_$($device.ProductId))"
}

Write-Host ""
Write-Host "Next steps (manual and elevated on purpose - see docs/design/usbtmc-transport.md):"
Write-Host "  1. Download Zadig from https://zadig.akeo.ie/ and run it as Administrator."
Write-Host "  2. Copy $zadigIniPath next to zadig.exe (or run zadig.exe from $OutputDirectory)."
Write-Host "  3. Device menu -> Load Preset Device -> pick one of the zadig-preset-*.cfg files above."
Write-Host "  4. Confirm the driver dropdown shows 'WinUSB', then click 'Install Driver' (or 'Replace Driver')."
Write-Host "  5. Repeat step 3-4 for each additional preset file - Zadig handles one device per run."
