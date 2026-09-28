MidiMaze (Windows, x64)
=======================

Quick start
  Start-MidiMaze.bat        starts the server; open http://localhost:5080

Other computers on your network
  Run MidiMaze\MidiMaze.Server.exe --urls http://*:5080 and allow it through the Windows firewall.

Run as a Windows service (starts with the machine)
  Elevated PowerShell:  .\Install-Service.ps1 [-Port 5080]
  Remove again:         .\Uninstall-Service.ps1

Behind a reverse proxy
  Pass WebSocket upgrades through (path /hub). To publish under a sub-path, set the PathBase
  setting (environment variable PathBase=/midimaze or in MidiMaze\appsettings.json).
