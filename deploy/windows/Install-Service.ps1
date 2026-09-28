# Installs MidiMaze as an auto-starting Windows service. Run from an elevated PowerShell.
#   .\Install-Service.ps1                      # listens on http://*:5080
#   .\Install-Service.ps1 -Port 8080
param(
    [int]$Port = 5080,
    [string]$Name = 'MidiMaze'
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'MidiMaze\MidiMaze.Server.exe'
if (-not (Test-Path $exe)) { throw "MidiMaze.Server.exe not found next to this script: $exe" }

if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
    throw "Service '$Name' already exists. Run Uninstall-Service.ps1 first."
}

New-Service -Name $Name `
    -BinaryPathName "`"$exe`" --urls http://*:$Port" `
    -DisplayName 'MidiMaze' `
    -Description 'MidiMaze multiplayer game server' `
    -StartupType Automatic | Out-Null

# Let clients on the LAN in.
New-NetFirewallRule -DisplayName "MidiMaze ($Port)" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null

Start-Service -Name $Name
Write-Host "MidiMaze is running as service '$Name' on port $Port."
