<#
.SYNOPSIS
  Reads the settings of USR-IOT serial-to-Ethernet bridges (USR-TCP232-302 and the RS232-2 variant) from their web UI.

.DESCRIPTION
  Read-only: only HTTP GETs of the status, IP, Serial Port and Expand Function pages. Nothing is written and the
  module is not rebooted. The pages publish their current values as `var name = value;` JavaScript, which this
  script parses (the same data the web form shows). Default credentials are admin / admin.

.EXAMPLE
  .\Get-UsrBridgeSettings.ps1 -HostName 192.168.0.108,192.168.0.109,192.168.0.110

.EXAMPLE
  .\Get-UsrBridgeSettings.ps1 -HostName 192.168.0.110 -Format Markdown   # a table ready to paste into known-configurations.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$HostName,
    [string]$UserName = 'admin',
    [string]$Password = 'admin',
    [ValidateSet('Object', 'Markdown', 'Json')][string]$Format = 'Object',
    [int]$TimeoutSeconds = 8
)

$ErrorActionPreference = 'Stop'

# `powershell -File script.ps1 -HostName a,b` delivers one "a,b" string; accept both spellings.
$HostName = @($HostName | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

$workModes = @{ 0 = 'UDP Client'; 1 = 'TCP Client'; 2 = 'UDP Server'; 3 = 'TCP Server'; 4 = 'Httpd Client' }
$parities = @{ 1 = 'None'; 2 = 'Odd'; 3 = 'Even'; 4 = 'Mark'; 5 = 'Space' }

function Get-Page([string]$Address, [string]$Page) {
    $token = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${UserName}:${Password}"))
    (Invoke-WebRequest -Uri "http://$Address/$Page.shtml" -Headers @{ Authorization = "Basic $token" } -UseBasicParsing -TimeoutSec $TimeoutSeconds).Content
}

function Get-Vars([string]$Html) {
    $vars = @{}
    foreach ($m in [regex]::Matches($Html, '(?m)^\s*var\s+(\w+)\s*=\s*(\d+)\s*;')) {
        $vars[$m.Groups[1].Value] = [int]$m.Groups[2].Value
    }

    $vars
}

function Get-Marker([string]$Html, [string]$Name) {
    $m = [regex]::Match($Html, "<!--#$Name-->([^<\r\n]*)")
    if ($m.Success) { $m.Groups[1].Value.Trim() }
}

function Join-Ip($v, [string]$Prefix) { "{0}.{1}.{2}.{3}" -f $v["${Prefix}1"], $v["${Prefix}2"], $v["${Prefix}3"], $v["${Prefix}4"] }

$results = foreach ($address in $HostName) {
    try {
        $status = Get-Page $address 'initialen'
        $ip = Get-Vars (Get-Page $address 'ipconfigen')
        $serialHtml = Get-Page $address 'sernet1'
        $serial = Get-Vars $serialHtml
        $expand = Get-Vars (Get-Page $address 'sernet2')

        [pscustomobject]@{
            Host           = $address
            Reachable      = $true
            Module         = Get-Marker $status 'modname'
            Mac            = Get-Marker $status 'macaddr'
            Dhcp           = $ip.staticip -eq 0
            Address        = Get-Marker $status 'ipaddr'
            StaticAddress  = Join-Ip $ip 'sip'
            Subnet         = Join-Ip $ip 'mip'
            Gateway        = Join-Ip $ip 'gip'
            Baud           = $serial.br
            DataBits       = $serial.bc
            Parity         = $parities[$serial.par]
            StopBits       = $serial.sb
            WorkMode       = $workModes[$serial.tnm]
            LocalPort      = $serial.tlp
            RemotePort     = $serial.trp
            RemoteAddress  = Join-Ip $serial 'tip'
            RfcLike        = $serial.srh -eq 1
            ResetOption    = $serial.sre -eq 1
            LinkOption     = $serial.srf -eq 1
            IndexOption    = $serial.srg -eq 1
            ShortConnection = $expand.srr -eq 1
            KickOldClient  = $expand.srq -eq 1
            BufferBeforeConnect = $expand.srt -eq 1
            UartSetParameter = $expand.sro -eq 1
            Error          = $null
        }
    }
    catch {
        [pscustomobject]@{ Host = $address; Reachable = $false; Error = $_.Exception.Message }
    }
}

switch ($Format) {
    'Json' { $results | ConvertTo-Json }
    'Markdown' {
        '| Host | Module | IP | Mode | Local port | UART | RFC2217-like | Notes |'
        '| --- | --- | --- | --- | --- | --- | --- | --- |'
        foreach ($r in $results) {
            if (-not $r.Reachable) { "| $($r.Host) | | | | | | | unreachable: $($r.Error) |"; continue }
            $ipText = if ($r.Dhcp) { "$($r.Address) (DHCP)" } else { "$($r.Address) (static)" }
            "| $($r.Host) | $($r.Module) | $ipText | $($r.WorkMode) | $($r.LocalPort) | $($r.Baud) $($r.DataBits)/$($r.Parity)/$($r.StopBits) | $(if ($r.RfcLike) { 'on' } else { 'off' }) | |"
        }
    }
    default { $results }
}
