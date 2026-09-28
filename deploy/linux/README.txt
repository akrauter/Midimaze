MidiMaze (Linux, x64)
=====================

Quick start
  ./start-midimaze.sh       starts the server; open http://localhost:5080

Run as a systemd service
  sudo useradd --system --no-create-home midimaze
  sudo mkdir -p /opt/midimaze && sudo cp -r MidiMaze/* /opt/midimaze/
  sudo chown -R midimaze: /opt/midimaze
  sudo cp midimaze.service /etc/systemd/system/
  sudo systemctl daemon-reload && sudo systemctl enable --now midimaze
  journalctl -u midimaze -f

Behind a reverse proxy
  Pass WebSocket upgrades through (path /hub). To publish under a sub-path, set PathBase=/midimaze
  (environment variable or appsettings.json).
