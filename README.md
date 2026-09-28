# MidiMaze

Browser-Multiplayer-Nachbau von *MIDI Maze* (Atari ST, 1987): Smileys jagen sich in einem
Ego-Perspektive-Labyrinth. ASP.NET Core (.NET 10) + SignalR, Raycaster im Client.

## Starten

```bash
dotnet run --project src/MidiMaze.Server
```

Öffnet auf `http://localhost:5080`. Für andere Rechner im Netz: `--urls http://*:5080`.
Tests: `dotnet test`.

## Spielen

- **Lobby:** Namen eingeben, einem Raum beitreten oder einen eigenen erstellen (Modus, Rundenlänge,
  Anzahl Bots). Die Raumliste aktualisiert sich live. Der Raum „Arena“ ist immer da; selbst
  erstellte Räume verschwinden, sobald der letzte Mensch geht.
- **Runden:** Jede Runde hat ein Zeitlimit. Danach zeigt eine Ergebnistafel den Sieger, nach 10 s
  beginnt eine neue Runde mit neuem Labyrinth und zurückgesetzten Punkten.
- **Modi:** *Jeder gegen jeden* oder *Teams* (Rot gegen Blau, automatisch ausgeglichen, kein
  Friendly Fire, Teamwertung = Kills).
- **Bots:** Computergegner füllen Räume auf und machen Platz, sobald Menschen beitreten.
  Insgesamt max. 16 Smileys pro Raum.
- **Treffer:** Ein Treffer nimmt 34 von 100 Lebenspunkten, der dritte erledigt den Gegner.
  Respawn nach 3 s an einer Stelle, die weit von allen anderen liegt.
  Score = Treffer + 5 pro Kill.
- **Sound:** synthetisierte Effekte (Web Audio), mit `N` oder dem Lautsprecher-Knopf stumm schalten.

| Taste | Aktion |
|---|---|
| W / S oder ↑ / ↓ | vor / zurück |
| ← / → oder Q / E | drehen |
| A / D | seitwärts |
| Leertaste oder Strg | schießen |
| M | Karte ein/aus |
| N | Ton ein/aus |
| Esc | zurück zur Lobby |

Auf Touch-Geräten erscheinen Steuerknöpfe (Pfeile + FEUER) im Bild.

## Architektur

```
src/MidiMaze.Server
  Game/RoomManager.cs      Lobby: alle Räume, Zuordnung Verbindung -> Raum
  Game/GameRoom.cs         ein Raum: Spieler, Bots, Schüsse, Treffer, Teams, Rundenzyklus (30 Hz)
  Game/BotBrain.cs         Bot-KI: Sichtlinie, Pfadsuche (BFS), unscharfes Zielen, Reaktionszeit
  Game/Movement.cs         ein Bewegungsschritt mit Kollision (Server und Client teilen die Logik)
  Game/MazeMap.cs          Labyrinth-Generator (Backtracking + Schleifen + offene Räume)
  Game/GameLoopService.cs  fester Tick für alle Räume, Snapshot pro Raum, Lobby-Push
  Hubs/GameHub.cs          SignalR: GetRooms, CreateRoom, Join, Leave, Input;
                           Server -> Client: Snapshot, Rooms
  wwwroot/js/
    movement.js            Port von Movement.cs (Client-Prediction)
    render.js              Raycaster, Smiley-Sprites, Minimap
    game.js                Lobby, Netzwerk, Prediction + Reconciliation, Interpolation, HUD
    sound.js               Soundeffekte
tests/MidiMaze.Tests       xUnit: Labyrinth, Bewegung, Räume, Runden, Teams, Bots, Lobby
```

- **Server-autoritativ:** Clients senden nur Eingaben (`seq`, vor/seitwärts/drehen, Feuer) pro
  30-Hz-Schritt. Ein Budget pro Spieler (~1 Eingabe pro Tick) verhindert Speed-Hacks.
- **Prediction:** Der Client bewegt den eigenen Smiley sofort und spielt bei jedem Snapshot die noch
  nicht bestätigten Eingaben ab dem Serverzustand nach (`ack`). Andere Spieler werden mit 100 ms
  Verzögerung interpoliert.
- **Snapshots** enthalten Phase, Restzeit, Teamwertung, Spieler, Schüsse und Treffer-Ereignisse.
  Karte und Roster kommen nur im ersten Snapshot nach einer Änderung (neue Runde, Beitritt/Abgang).
- **Ändert man Bewegungswerte,** müssen `Movement.cs` und `movement.js` gleich bleiben. Die Konstanten
  kommen vom Server (Welcome-Nachricht).

## Betrieb

### Docker

```bash
docker compose up -d --build     # http://localhost:5080
```

Das Image läuft als Nicht-Root-Benutzer auf Port 8080 und hat einen Healthcheck
(`dotnet MidiMaze.Server.dll --healthcheck` fragt `/health` ab, das aspnet-Image hat kein curl).

### Windows-Dienst / systemd

Dieselbe Binärdatei läuft eigenständig, als Windows-Dienst und als systemd-Unit.
Skripte liegen in `deploy/windows` (`Install-Service.ps1`, `Uninstall-Service.ps1`,
`Start-MidiMaze.bat`) und `deploy/linux` (`midimaze.service`, `start-midimaze.sh`).
Die CI baut daraus die Release-Pakete (`MidiMaze-win-x64-*.zip`, `MidiMaze-linux-x64-*.tar.gz`).

### Konfiguration

| Einstellung | Wirkung |
|---|---|
| `--urls` / `ASPNETCORE_URLS` | Adresse und Port (Standard `http://localhost:5080`, im Container `http://+:8080`) |
| `PathBase` (Env oder `appsettings.json`) | Unterpfad hinter einem Reverse Proxy, z. B. `/midimaze` |

Hinter einem Reverse Proxy müssen WebSocket-Upgrades (Pfad `/hub`) durchgereicht werden.
Der Client verwendet nur relative URLs, deshalb funktioniert ein Unterpfad ohne weitere Anpassung.

### CI

`.github/workflows/ci.yml` (nach dem Muster von GatewayHub): Build und Test bei jedem Push/PR,
auf `master` zusätzlich Docker-Image nach Docker Hub (`akrauter/midimaze`, Secrets
`DOCKERHUB_USERNAME`/`DOCKERHUB_TOKEN`) sowie Windows- und Linux-Release auf GitHub.
