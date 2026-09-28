@echo off
cd /d "%~dp0MidiMaze"
echo Starting MidiMaze - open http://localhost:5080 in your browser once it says "Application started".
echo For other computers on your network use:  MidiMaze.Server.exe --urls http://*:5080
echo Press Ctrl+C to stop.
MidiMaze.Server.exe
pause
