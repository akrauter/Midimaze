#!/usr/bin/env bash
cd "$(dirname "$0")/MidiMaze"
echo "Starting MidiMaze - open http://localhost:5080 in your browser once it says \"Application started\"."
echo "For other computers on your network use:  ./MidiMaze.Server --urls http://0.0.0.0:5080"
echo "Press Ctrl+C to stop."
./MidiMaze.Server
