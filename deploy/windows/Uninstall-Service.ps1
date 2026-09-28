# Removes the MidiMaze Windows service and its firewall rule. Run from an elevated PowerShell.
param(
    [string]$Name = 'MidiMaze'
)

$ErrorActionPreference = 'Stop'

$service = Get-Service -Name $Name -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $Name -Force }
    sc.exe delete $Name | Out-Null
    Write-Host "Service '$Name' removed."
} else {
    Write-Host "Service '$Name' does not exist."
}

Get-NetFirewallRule -DisplayName 'MidiMaze (*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
