# Ludus

Ludus ist eine selbst gehostete Verwaltung für Retro-ROM-Bibliotheken nach dem Servarr-Prinzip: Spiele überwachen, Releases bewerten, Downloads an einen Client übergeben und abgeschlossene Dateien in frei wählbare Root Folders importieren.

ASP.NET Core und EF Core 10 bilden das Backend; SQLite läuft im WAL-Modus. Das React-/TypeScript-/Tailwind-Frontend wird im Produktionsbetrieb vom selben Dienst ausgeliefert. SignalR aktualisiert Warteschlange, Importe, Scans und Systemzustand. Die Oberfläche ist auf Deutsch und Englisch verfügbar.

## Funktionen

- IGDB mit Twitch-OAuth und ScreenScraper.fr v2, getrennte UI-/Metadatensprache, lokale Cover und plattformbezogener Provider-Fallback.
- No-Intro-/Redump-Release-Parsing, PlayStation-Serials, Switch-Title-IDs und Base-/Update-/DLC-Zuordnung.
- Logiqx-DAT-Import, CRC32/MD5/SHA1 und Verifikationsstatus.
- Torznab/Newznab über Prowlarr, Jackett oder einen kompatiblen Indexer; RSS und interaktive Suche einschließlich Ablehnungsgründen.
- Plattformgebundene Qualitätsprofile, wiederverwendbare Custom Formats, Mindestscore und Upgrade-Cutoff.
- qBittorrent, Transmission und SABnzbd, feste Kategorie/Label `ludus`, Remote Path Mappings und Completed Download Handling.
- ZIP/7z, Hardlinks mit Kopier-Fallback, CUE/BIN/GDI-Sets, M3U, Ordnerspiele und Recycling Bin.
- Forms- oder HTTP-Basic-Anmeldung, separater API-Key für Integrationen, CSRF-Schutz, verschlüsselte Credentials und signierte Medien-Links.
- Health Checks, Kalender, Zeitpläne, Backups/Restore, bereinigter Diagnose-Export sowie Discord-, Gotify-, Telegram- und generische Webhooks.

## Betriebsvarianten

Für GitHub stehen ein wiederholbarer Release-Build und ein Actions-Workflow bereit: [Builds erstellen und veröffentlichen](docs/releases.md). Der Build erzeugt portable Anwendungspakete mit fertigem Frontend, ein Quellcode-ZIP und SHA256-Prüfsummen.

| Variante | Geeignet für | Installation |
| --- | --- | --- |
| Docker Compose | Bestehender Container-Host oder Debian-VM unter Proxmox | Lokal aus diesem Checkout bauen |
| Natives Debian-LXC | Eigener Ludus-Container ohne Docker | `install-lxc.sh` innerhalb des LXC ausführen |
| Entwicklungsbetrieb | Arbeit am Quellcode | .NET-Backend und Vite separat starten |

Die Beispiele verwenden Port `8080`, UID/GID `1000` und eigene Beispielpfade. Passe Adressen, IDs und Verzeichnisse an deine Installation an. Für einen neuen LXC sind 2 CPU-Kerne, 4 GiB RAM und 20 GiB Systemplatte ein sinnvoller Startwert für den lokalen Build; ROM- und Download-Speicher kommen hinzu.

## Docker Compose

Voraussetzungen: Docker Engine, Docker Compose und ein vollständiger Checkout. Für den Hardlink-Override ist Compose **2.24.4 oder neuer** erforderlich, weil die Volume-Liste mit `!override` ersetzt wird. [Docker-Dokumentation](https://docs.docker.com/reference/compose-file/merge/#replace-value)

### Empfohlen: gemeinsames Medienverzeichnis

Downloads und Bibliothek sollen denselben Mount und dasselbe Dateisystem verwenden:

```text
data/
├── config/                 → /config
└── media/                  → /data
    ├── downloads/
    │   ├── torrents/
    │   └── usenet/
    └── roms/
        ├── nes/
        ├── snes/
        └── ps1/
```

Im Repository-Stamm auf einem Linux-Docker-Host:

```bash
cp .env.example .env
mkdir -p data/config data/media/downloads/torrents data/media/downloads/usenet
mkdir -p data/media/roms/nes data/media/roms/snes data/media/roms/ps1
sudo chown 1000:1000 data/media/downloads data/media/downloads/torrents data/media/downloads/usenet
sudo chown 1000:1000 data/media/roms data/media/roms/nes data/media/roms/snes data/media/roms/ps1
sudo chmod 2775 data/media/downloads data/media/downloads/torrents data/media/downloads/usenet
sudo chmod 2775 data/media/roms data/media/roms/nes data/media/roms/snes data/media/roms/ps1
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml config
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml up -d --build
```

Diese Eigentümerbefehle sind für die gerade angelegten Verzeichnisse gedacht. Bestehende Sammlungen nicht pauschal umberechtigen. Stelle vorhandene Freigaben über passende Benutzer/Gruppen oder ACLs bereit.

Die Standardwerte in `.env` sind:

| Variable | Standard | Bedeutung |
| --- | --- | --- |
| `PUID`, `PGID` | `1000`, `1000` | Prozess-ID und primäre Gruppe |
| `UMASK` | `002` | Neue Mediendateien können von der gemeinsamen Gruppe geschrieben werden |
| `LUDUS_BIND_ADDRESS` | `127.0.0.1` | Host-Adresse für den veröffentlichten Port |
| `LUDUS_PORT` | `8080` | Host-Port; intern bleibt der Container auf `8080` |
| `LUDUS_CONFIG_PATH` | `./data/config` | Datenbank, Schlüssel, Logs und Sicherungen |
| `LUDUS_DATA_PATH` | `./data/media` | Gemeinsamer Medien-Mount beim Hardlink-Override |
| `LUDUS_DOWNLOADS_PATH` | `./data/downloads` | Nur für das einfache Compose-Layout |
| `LUDUS_ROMS_PATH` | `./data/roms` | Nur für das einfache Compose-Layout |

Für direkten LAN-Zugriff `LUDUS_BIND_ADDRESS=0.0.0.0` setzen und den Zugriff auf den vorgesehenen Netzbereich begrenzen. Bei einem lokalen Reverse Proxy kann `127.0.0.1` bleiben. Der Healthcheck ist ohne Anmeldung erreichbar:

```bash
curl --fail http://127.0.0.1:8080/api/v1/system/status
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml ps
```

Erwartete Antwort: `{"status":"ok"}`. Der minimale Check bestätigt einen gestarteten API-Dienst; konfigurierte Clients, Indexer und Root Folders werden unter **System → Status & Zustand** geprüft.

### Einfaches Layout mit drei Mounts

Wenn Hardlinks nicht benötigt werden, genügt `docker-compose.yml`:

```bash
mkdir -p data/config data/downloads data/roms
sudo chown 1000:1000 data/downloads data/roms
sudo chmod 2775 data/downloads data/roms
docker compose up -d --build
```

Dabei sind `/downloads` und `/roms` separate Bind Mounts. Selbst bei derselben Host-Platte können Hardlinks an dieser Mount-Grenze scheitern; Ludus kopiert dann. Der Entrypoint ändert rekursiv ausschließlich `/config`, ohne Symlinks zu verfolgen oder untergeordnete Mounts zu überschreiten. `/config/keys` erhält Modus `0700`. Downloads und ROMs werden nicht rekursiv mit `chown` bearbeitet.

## Proxmox: natives Debian-LXC

### 1. Container und Medien bereitstellen

Im Proxmox-Assistenten einen **unprivilegierten Debian-13-Container** anlegen; Debian 12 wird ebenfalls unterstützt. Eine feste IP oder DHCP-Reservierung erleichtert die Client-Konfiguration. Das Skript installiert weder Docker noch verändert es die Proxmox-Host-Konfiguration. Für native Ludus-Prozesse ist Docker-Nesting nicht erforderlich.

Lade das Debian-Template über die Proxmox-Oberfläche. Prüfe vor einem Bind Mount freie Mount-Slots und die UID/GID-Abbildung. Bind Mounts werden nicht wie verwaltete Container-Volumes automatisch in `vzdump` gesichert. [Proxmox-Container-Dokumentation](https://github.com/proxmox/pve-docs/blob/master/pct.adoc)

Beispiel für einen neu angelegten Container mit ID `120`: Auf dem **Proxmox-Host**, bei freiem `mp0`:

```bash
pct config 120
pct exec 120 -- cat /proc/self/uid_map
pct exec 120 -- cat /proc/self/gid_map
```

Bei der üblichen Abbildung `0 → 100000` entspricht die Container-ID `1000` der Host-ID `101000`. Andere Abbildungen benötigen andere Eigentümer. Erst nach dieser Prüfung neue Host-Verzeichnisse anlegen:

```bash
mkdir -p /srv/ludus-media
install -d -o 101000 -g 101000 -m 2775 /srv/ludus-media/downloads /srv/ludus-media/roms
pct set 120 -mp0 /srv/ludus-media,mp=/data
```

Beide Medienverzeichnisse liegen im selben Mount. Separate ZFS-Datasets, unterschiedliche Datenträger oder getrennte Mounts bleiben separate Hardlink-Grenzen. `/config` sollte auf lokalem persistentem Speicher mit zuverlässigen SQLite-Locks liegen; kein gemeinsam von mehreren Ludus-Instanzen geöffnetes Datenbankverzeichnis verwenden.

### 2. Checkout übertragen und installieren

Einen vertrauenswürdigen, vollständigen Ludus-Checkout beispielsweise nach `/root/Ludus` **im Container** übertragen. Für einen Transport ohne Build-Caches und lokale Betriebsdaten kann auf einem Linux-Rechner im Checkout ein Quellarchiv erstellt werden:

```bash
tar --exclude=node_modules --exclude=bin --exclude=obj --exclude=dist --exclude=wwwroot --exclude='.env*' \
  -czf /tmp/ludus-source.tar.gz \
  install-lxc.sh global.json Directory.Build.props Directory.Packages.props NuGet.Config src frontend
```

Dieses Archiv in den Container kopieren und dort entpacken. Beispiel im Container, nachdem es unter `/root/ludus-source.tar.gz` liegt:

```bash
mkdir -p /root/Ludus
tar -xzf /root/ludus-source.tar.gz -C /root/Ludus
cd /root/Ludus
bash install-lxc.sh --media-dir /data --uid 1000 --gid 1000 --bind 0.0.0.0
```

Das Skript muss als root **im Ziel-LXC**, nicht auf dem Proxmox-Host, laufen. Es prüft Debian 12/13, amd64/arm64 und systemd. Der Microsoft-Paketfeed stellt .NET SDK und ASP.NET Runtime 10 bereit; Node 24 wird von `nodejs.org` per HTTPS geladen und gegen die veröffentlichte SHA256-Liste geprüft. Die Auswahl eines konkreten Node-Releases ist über `--node-version 24.22.3` möglich. [Microsoft: .NET auf Debian](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian), [offizielles Node-24-Verzeichnis](https://nodejs.org/dist/latest-v24.x/)

Der Build läuft als separater Benutzer `ludus-build` mit eigenen temporären Caches. Der Dienst läuft als `ludus`. Falls UID `1000` bereits zu einem anderen Benutzer gehört, eine freie `--uid` wählen und die Medienrechte dazu passend vorbereiten. Ein bestehender GID kann für die gemeinsame Mediengruppe verwendet werden; vorhandene Konten werden nicht umnummeriert.

Der Build-Benutzer erhält pro Installation ein beschreibbares Home-Verzeichnis sowie eigene XDG- und temporäre Verzeichnisse. Vor dem Frontend-Build prüft der Installer mit `dotnet --version` die SDK-Initialisierung unter diesem Benutzer. Ein Fehler bei `NuGet-Migrations` betrifft die .NET-/NuGet-Initialisierung, nicht die Ludus-Datenbankmigrationen. Die .NET-10-Runtime verwendet unter Linux für benannte Mutexe weiterhin `/tmp`; ein eigenes `TMPDIR` allein behebt dortige Rechte- oder Containerprobleme nicht. [Runtime-Quellcode](https://github.com/dotnet/runtime/blob/v10.0.15/src/coreclr/pal/src/init/pal.cpp#L1116)

Ohne `--media-dir` sind `/downloads` und `/roms` die Standardpfade. `--config-dir`, `--downloads-dir`, `--roms-dir`, `--port` und `--bind` sind ebenfalls verfügbar; `bash install-lxc.sh --help` zeigt alle Optionen. Installationspfade müssen kanonisch sein und dürfen weder Leerzeichen noch Symlink-Komponenten enthalten.

### 3. Dienst und Verzeichnisse

| Pfad | Verwendung |
| --- | --- |
| `/opt/ludus/releases/<Zeitstempel>-<PID>` | Fertig gebautes Release, root gehört der Code |
| `/opt/ludus/current` | Atomar gewechselter Symlink auf das aktive Release |
| `/opt/ludus/tools` | Verifizierte Node-Buildwerkzeuge |
| `/etc/ludus/ludus.env` | Vom Installer erzeugte Laufzeitumgebung |
| `/etc/ludus/install-state` | Persistierte Installer-Optionen, Modus `0600` |
| `/etc/systemd/system/ludus.service` | Erzeugte systemd-Unit |
| `/opt/ludus/rollback` | Offline-Konfigurationsarchive vor Updates |
| `/config` | Standard für persistente Anwendungsdaten |

```bash
systemctl status ludus --no-pager
journalctl -u ludus -n 100 --no-pager
curl --fail http://127.0.0.1:8080/api/v1/system/status
```

Die Unit startet bei Fehlern und nach einem kontrollierten Restore-Exit neu. Sie verwendet `NoNewPrivileges`, leere Capabilities, private temporäre Dateien und ein weitgehend schreibgeschütztes Dateisystem. Schreibzugriff erhalten Konfiguration und Medien. Mit `--media-dir /data` wird `/data` gemeinsam freigegeben, damit die systemd-Mount-Namespace keine separate Schreibfreigabe pro Medienunterordner benötigt. [systemd-Ausführungsoptionen](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html)

Zusätzliche Root Folders außerhalb der freigegebenen Medienpfade benötigen neben Dateirechten einen Drop-in. Beispiel für eine weitere Sammlung unter `/mnt/arcade`:

```bash
systemctl edit ludus
```

```ini
[Service]
ReadWritePaths=/mnt/arcade
```

```bash
systemctl daemon-reload
systemctl restart ludus
```

Auch ein abweichender Papierkorb-Pfad benötigt Schreibfreigabe. Für Hardlinks Downloads und Zielbibliothek möglichst unter derselben freigegebenen Medienwurzel halten. Eigene Anpassungen gehören in Drop-ins; die erzeugte Unit und `ludus.env` werden bei Updates neu geschrieben.

## Erste Einrichtung

1. Oberfläche auf Port 8080 öffnen und das Administratorkonto mit Benutzername und einem mindestens zwölf Zeichen langen Passwort anlegen und bestätigen. Die Ersteinrichtung verwendet immer **Forms** mit der Ludus-Anmeldeseite. **Basic** kann anschließend unter **Allgemein & Sicherheit** aktiviert werden, verwendet den nativen Browserdialog und darf nur über HTTPS eingesetzt werden.
2. Unter **Medienverwaltung** bestehende Root Folders hinterlegen. Der Seed legt zunächst einen Root `ROMs` für das konfigurierte ROM-Verzeichnis an. Entweder diesen gemeinsamen Root oder nicht überlappende plattformspezifische Roots verwenden. Einen ungenutzten gemeinsamen Seed-Root vor dem Einrichten seiner Unterordner als separate Roots entfernen.
3. **Metadaten** einrichten: IGDB benötigt Twitch Client-ID und Client-Secret; ScreenScraper Entwickler-ID/-Passwort und gegebenenfalls Benutzername/-Passwort. Aktivieren, speichern und testen. Die bevorzugte Metadatensprache ist von der UI-Sprache unabhängig. Plattform-System-IDs, primären Provider und Fallback in der Plattformverwaltung prüfen.
4. Unter **Custom Formats** gemeinsame Erkennungsregeln pflegen. Unter **Profile** für jede Plattform mindestens ein Profil mit eigenen Scores anlegen. Das Standardprofil bevorzugt Europe (`+100`) vor USA (`+80`), bewertet CHD mit `+50` und Beta/Demo/Proto stark negativ. Nach Erreichen des Cutoffs werden keine weiteren Basis-ROM-Upgrades gesucht.
5. Indexer und Download-Client einrichten. Mindestalter, Maximalgröße, RSS-Intervall, Completed Download Handling und die beiden getrennten Ersatzsuche-Schalter prüfen; anschließend **System → Status & Zustand** öffnen.
6. Unter **UI** Kalender, Datums-/Zeitformat, Theme und Oberflächensprache festlegen. Ein Wechsel der Oberflächensprache verändert die Metadatensprache nicht.
7. Vorhandene ROMs über **Bibliothek scannen** aufnehmen. Bei mehrdeutigen Endungen wie `.iso` eine Plattform auswählen. Für neue Spiele Plattform, Root Folder, passendes Profil und Monitoring festlegen.

Root Folders müssen bereits existieren und für Ludus zugänglich sein. Das Umhängen eines Spiels mit vorhandenen Dateizuordnungen ist kein automatischer Verschiebeauftrag. Ein Scan kann eindeutig verschobene vollständige Dateisätze wiederfinden; der bisherige Root muss erreichbar sein.

## Prowlarr und Jackett

Ludus nutzt die einzelnen Torznab-/Newznab-Endpunkte. Es gibt derzeit keinen eigenen Prowlarr-Application-Sync-Adapter; die Einträge werden in Ludus angelegt.

1. Indexer in Prowlarr konfigurieren und dort testen.
2. Den angezeigten Torznab-/Newznab-Endpunkt des jeweiligen Indexers kopieren. Ein Beispiel für Indexer-ID `1` ist `http://prowlarr:9696/1/api`; der tatsächliche Host muss aus Ludus erreichbar sein.
3. In Ludus **Einstellungen → Indexer → Hinzufügen** öffnen. Die URL ohne Query-String eintragen und den Prowlarr-API-Key in das separate Secret-Feld setzen.
4. Für Torrents `Torznab` und `Torrent`, für Usenet `Newznab` und `Usenet` wählen. RSS, automatische und interaktive Suche nach Bedarf aktivieren.
5. Spielekategorien anhand der Capabilities wählen. Die Plattformen starten mit Standardkategorien im Bereich `1000`/`1010–1080`; nicht jeder Indexer bietet jede Kategorie an. Speichern und in Ludus testen.

Prowlarr stellt standardisierte Indexer-Endpunkte bereit und ordnet anbieterspezifische Kategorien den Standardkategorien zu. [Prowlarr-Indexer](https://github.com/Servarr/Wiki/blob/master/prowlarr/indexers.md), [Prowlarr-Einstellungen](https://github.com/Servarr/Wiki/blob/master/prowlarr/settings.md)

Für Jackett ebenfalls einen einzelnen Indexer-Endpunkt verwenden, beispielsweise `http://jackett:9117/api/v2.0/indexers/myindexer/results/torznab/api`, und den Key separat hinterlegen. `myindexer` steht hier für die tatsächliche Kennung aus Jackett. Ludus ergänzt `/api`, wenn die konfigurierte URL nicht bereits damit endet.

Indexer-Such- und Download-URLs müssen auch für den jeweiligen Download-Client erreichbar sein. Ein Docker-Dienstname funktioniert nur in passenden gemeinsamen Netzwerken; `localhost` bezeichnet im Container immer diesen Container.

## Download-Clients

Alle Client-Einträge werden unter **Einstellungen → Download-Clients** angelegt, gespeichert und anschließend getestet. Ludus verarbeitet nur selbst registrierte Aufträge, die zur Kategorie beziehungsweise zum Label `ludus` gehören. Es gibt kein Watch-Folder-Polling.

| Client | Beispiel für Basis-URL | Zugang und Isolation |
| --- | --- | --- |
| qBittorrent | `http://qbittorrent:8080` | Web-UI-Benutzer/Passwort; Kategorie `ludus` vorher anlegen und Speicherpfad einstellen |
| Transmission | `http://transmission:9091` | RPC-Benutzer/Passwort; Transmission 4 mit RPC ≥ 17; Label `ludus` wird beim Hinzufügen gesetzt |
| SABnzbd | `http://sabnzbd:8080` | API-Key; Kategorie `ludus` vorher anlegen und fertigen Download-Pfad festlegen |

Für Transmission weder fremde Labels noch globale Queue-Einträge ändern; Ludus übernimmt die Label-Isolation. Die Basis-URL kann auch auf `/transmission/rpc` enden. Downloads mit mehreren Dateien brauchen einen gemeinsamen Inhaltsordner; der gesamte Download-Root ist kein zulässiger Importauftrag.

Beispielpfade im Download-Client: `/data/downloads/torrents` für Torrents und `/data/downloads/usenet` für SABnzbd. Der Client und Ludus müssen denselben Inhalt sehen. Für qBittorrent/Transmission kann nach Import das Tag/Label `ludus-imported` gesetzt werden. SABnzbd hat keinen entsprechenden frei veränderbaren History-Tag.

Torrents bleiben für das Seeden erhalten. Bei aktivierter Bereinigung wartet Ludus auf die vom Adapter erkannten Seed-Ziele; erfolgreich bereinigte Aufträge löschen die Quelldateien nicht. Temporäre Tracker-/Netzwerkfehler erhalten sieben Tage Cooldown, defekte Archive permanente Sperren. Fehler durch lokale Rechte oder fehlende Mounts sollen behoben werden; sie machen ein Release nicht automatisch dauerhaft unbrauchbar.

**Completed Download Handling** kann deaktiviert werden, ohne Queue und manuellen Import abzuschalten. Die Herkunft eines Grabs wird gespeichert: automatische und interaktiv gestartete Downloads verwenden getrennte Schalter für eine Ersatzsuche. Eine Ersatzsuche übernimmt Plattform und Qualitätsprofil, respektiert Blocklist/Cooldown und wählt nicht erneut dasselbe Release.

## Remote Path Mappings und Hardlinks

Ein Mapping übersetzt einen vom Client gemeldeten Pfad; es bindet keinen Datenträger ein. Beispiel:

| Feld | Wert |
| --- | --- |
| Client-Basis-URL | `http://192.168.1.40:8080` |
| Mapping-Host | `192.168.1.40` (ohne Port) |
| Remote-Pfad | `/downloads` |
| Lokaler Pfad | `/data/downloads` |

Damit wird `/downloads/torrents/Game/Game.nes` zu `/data/downloads/torrents/Game/Game.nes`. Der lokale Pfad muss tatsächlich vorhanden sein und auf dieselben Daten zeigen. Ein bereits überall identischer `/data`-Pfad benötigt kein Mapping. Bei mehreren passenden Präfixen wird das längste verwendet.

Hardlinks benötigen dasselbe Dateisystem, einen kompatiblen Mount-Aufbau und passende Rechte. Ein gemeinsamer Mount für Downloads und Bibliothek sowie konsistente Gruppenrechte entspricht dem Servarr-Speichermodell. [Servarr Docker Guide](https://wiki.servarr.com/docker-guide)

Ein praktischer Test im nativen LXC als Dienstbenutzer, ohne bestehende Dateien anzufassen:

```bash
runuser -u ludus -- bash -c '
  set -euo pipefail
  source_dir=$(mktemp -d /data/downloads/.ludus-link-test.XXXXXXXX)
  target_dir=$(mktemp -d /data/roms/.ludus-link-test.XXXXXXXX)
  trap '\''rm -f -- "$source_dir/probe" "$target_dir/probe"; rmdir -- "$source_dir" "$target_dir"'\'' EXIT
  printf test > "$source_dir/probe"
  ln "$source_dir/probe" "$target_dir/probe"
  stat -c "%d:%i links=%h %n" "$source_dir/probe" "$target_dir/probe"
'
```

Bei Erfolg stimmen Geräte- und Inode-Nummer überein und der Link-Zähler ist `2`. Dieser Test prüft die Dateirechte und Host-Mounts; für die zusätzliche systemd-Namespace nach einem tatsächlichen Ludus-Import ebenfalls Quell- und Zieldatei mit `stat` vergleichen. Ein `EXDEV` bedeutet Mount-/Dateisystemgrenze; `EPERM`/`EACCES` weist auf fehlende Rechte oder fehlende Hardlink-Unterstützung hin. Ludus verwendet dann den sicheren Kopier-Fallback, was zusätzlichen Speicher beansprucht.

Hardlinks sind dieselbe Datei unter zwei Namen. ROM-Inhalte nach dem Import nicht an Ort und Stelle patchen, solange die Download-Kopie unverändert seeden soll.

Unter **Medienverwaltung** steuern „Leere Spielordner erstellen/löschen“ ausschließlich von Ludus erfasste Spielordner innerhalb eines Root Folders. Root Folders, nicht leere Ordner, fremde Verzeichnisse und Ziele außerhalb der Bibliothek werden nie entfernt. Die Freiplatzprüfung läuft auf dem tatsächlichen Zielvolume und berücksichtigt Dateigröße plus konfigurierten Restfreiraum; ein erfolgreicher Hardlink benötigt keinen zusätzlichen Inhaltsplatz. Unix-Datei-/Ordnerrechte und eine optionale Gruppe wirken nur unter Linux. Bei Hardlinks ändert Ludus Modus und Gruppe nicht, weil Quelle und Ziel denselben Inode verwenden; ansonsten gelten konfigurierte Rechte zusammen mit der Prozess-UMASK.

## Metadatensprachen

Die Spieldetailseite zeigt verfügbare Fassungen und die tatsächlich verwendete Sprache getrennt für Titel und Beschreibung. Fehlende Felder fallen nachvollziehbar auf eine vorhandene Fassung zurück; ein lokalisierter Titel kann daher mit einer anderssprachigen Beschreibung kombiniert werden. Refresh und Cache behalten die übrigen Sprachfassungen.

IGDB liefert über `game_localizations` lokalisierte Namen nach Region, aber keine entsprechend lokalisierten Beschreibungen. ScreenScraper liefert regiongebundene Namen sowie sprachgebundene `synopsis_*`- und Genre-Felder; die tatsächlich vorhandenen Fassungen unterscheiden sich je Spiel. Siehe [IGDB API](https://api-docs.igdb.com/index.html) und [ScreenScraper Web API v2](https://www.screenscraper.fr/webapi2.php).

## Connect-Benachrichtigungen

Unter **Einstellungen → Connect** können mehrere benannte Discord-Webhooks, Gotify-Instanzen, Telegram-Bots und generische HTTP-Webhooks eingerichtet werden. Pro Verbindung stehen Aktivierung, Ereignisauswahl und eine echte Testnachricht zur Verfügung. Unterstützte Ereignisse sind mindestens „an Download-Client übergeben“, „Import erfolgreich“, „Import fehlgeschlagen“ und „Download fehlgeschlagen“. Zustellfehler werden separat wiederholt und ändern keinen erfolgreichen Importstatus.

Für Gotify ist die Instanz-URL plus App-Token erforderlich. Telegram verwendet die API-Basis-URL, Bot-Token und Chat-ID. Token, Webhook-URL und optionale Authorization werden verschlüsselt gespeichert und in Diagnoseausgaben bereinigt. Der generische Webhook erhält per `POST` JSON in dieser Form:

```json
{
  "eventType": "Imported",
  "occurredAtUtc": "2026-11-27T12:00:00Z",
  "game": { "title": "Beispielspiel" },
  "release": { "title": "Example.Game.Europe" }
}
```

## DAT-Verifikation, Importe und Benennung

Unter **Einstellungen → DAT Files** eine offizielle Logiqx-XML-`.dat` der passenden Plattform hochladen. Die Upload-Grenze beträgt 256 MiB. Anschließend scannen, um vorhandene Dateien erneut zu prüfen. `[!]` kennzeichnet einen passenden DAT-Treffer; nicht passende oder unbekannte Dumps bleiben als Modified/Unverified sichtbar. Ein DAT-Status ersetzt keine Profilbewertung.

Plattformoptionen steuern erlaubte Endungen, Ordnerspiele und Archiv-Entpacken. ZIP/7z werden über SharpCompress verarbeitet; ein zusätzliches `7z`-Programm ist nicht erforderlich. Archive haben konfigurierbare Eintrags-/Größenlimits. CUE/GDI-Referenzen und Multi-Disc-Sets müssen vollständig sein; Switch-Updates/DLCs werden anhand ihrer Identität dem Basisspiel zugeordnet.

Das Standard-Schema ist:

```text
{RootFolder}/{Game Title} ({Year})/{Game Title} ({Region}).{ext}
```

Root Folder ist die Spielzuordnung. Bei zusammengehörigen Track-/Ordner-Sets bleiben notwendige interne Namen erhalten. Upgrades sichern ersetzte Dateien im Recycling Bin. Nicht abgeschlossene Import-Journale unter `/config/Imports` oder deren Recycling-Dateien nicht manuell löschen.

## Security, Reverse Proxy und Dashboards

Alle REST-Aufrufe liegen unter `/api/v1`. Externe Anwendungen senden weiterhin den Integrationsschlüssel im Header `X-Api-Key`; er ist kein Browser-Login. Im Modus **Forms** verwendet die Oberfläche eine HttpOnly-Cookie-Session und CSRF-Header, ohne eine Basic-Challenge auszulösen. Im Modus **Basic** sendet Ludus `WWW-Authenticate: Basic`; Browser und SignalR verwenden dieselben Administrator-Zugangsdaten. Der Key wird unter **Allgemein & Sicherheit** angezeigt/zurückgesetzt. Ein Reset widerruft alte Sessions und signierte Medien-Links. Credentials in der Datenbank werden mit dem Keyring unter `/config/keys` verschlüsselt; dieser Ordner gehört zur persistenten Sicherung.

Der Reverse Proxy soll HTTPS terminieren, den ursprünglichen `Host` erhalten, `X-Forwarded-For` und `X-Forwarded-Proto` setzen und WebSockets für `/hubs/activity` weiterleiten. In Ludus nur das tatsächlich verwendete private Proxy-Netz und die passende Hop-Anzahl freigeben. Proxy-Vertrauen ersetzt keine Authentifizierung. Ein separater Hostname mit Weiterleitung ab `/` ist der vorgesehene Betrieb; ein beliebiger URL-Unterpfad ist nicht konfiguriert.

Falls die Administrator-Zugangsdaten vergessen wurden, das Konto ausschließlich lokal auf dem Server neu setzen. Der Befehl fragt Benutzername, Passwort und Passwortbestätigung interaktiv ab; Zugangsdaten stehen weder in der Kommandozeile noch in der Prozessliste oder in Logs. Ludus muss gestoppt sein, damit der Befehl die Instanz exklusiv sperren kann:

```bash
systemctl stop ludus
/opt/ludus/current/Ludus.Api account set
systemctl start ludus
```

Der Befehl verwendet denselben ASP.NET-Core-`PasswordHasher` wie die Webanwendung, stellt den Forms-Modus ein und invalidiert bestehende Sitzungen. Bei einem manuellen portablen Deployment mit gesetzten `Ludus__*`-Umgebungsvariablen lautet der entsprechende Aufruf `dotnet Ludus.Api.dll account set`.

Für Homarr/Homepage eignen sich der anonyme Status-Endpunkt und authentifizierte API-Abfragen wie `/api/v1/system/info`, `/api/v1/system/health` oder `/api/v1/activity/queue`. Ein speziell für Ludus bereitgestelltes Dashboard-Widget ist nicht Teil dieses Repositories. Signierte Medien-URLs enthalten zeitlich begrenzte Zugriffstoken; Proxy-Logs sollten deren Query-Strings ebenfalls nicht dauerhaft sammeln.

## Sicherungen, Restore und Updates

### Anwendungssicherungen

**System → Backups** erstellt konsistente SQLite-Snapshots inklusive Datenbank, Konfiguration, Keyring und Manifest. Geplante ZIPs liegen unter `/config/Backups/scheduled`, manuelle/importierte unter `Backups/manual`. Die Retention gilt für geplante Sicherungen. ROMs, Downloads, Covercache, Logs und Bootstrap-Key sind nicht enthalten.

Backups enthalten Datenbank **und** Entschlüsselungsschlüssel und müssen vertraulich aufbewahrt werden. Zusätzlich ROMs und Downloads mit deinem Speichersystem sichern; Proxmox-Bind-Mounts benötigen eine eigene Sicherungsstrategie. Eine aktive WAL-Datenbank nicht durch Kopieren nur der `ludus.db` sichern: UI-Backup verwenden oder den Dienst für eine vollständige Offline-Kopie stoppen.

Ein ZIP kann in einer neuen Ludus-Instanz importiert werden. **Wiederherstellen** prüft das Archiv, bereitet den Austausch vor und beendet den Dienst. Compose beziehungsweise `Restart=always` startet ihn erneut; beim Start wird der Restore vor Migrationen und Hintergrundjobs angewendet. Danach erneut anmelden, Bibliothek scannen und Client-Queue prüfen. ROM-Dateien werden nicht auf ihren früheren Stand zurückgesetzt.

### Docker-Update

Vor einem Update ein UI-Backup erstellen, dann den geprüften neuen Quellstand bereitstellen:

```bash
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml build --pull
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml up -d
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml ps
```

Beim einfachen Layout die beiden `-f`-Argumente weglassen. Migrationen laufen automatisch beim App-Start. Ein älteres Image nicht ungeprüft gegen eine bereits migrierte Datenbank starten.

Beim Upgrade werden vorhandene Benutzernamen und Passwort-Hashes unverändert übernommen; eine erneute Einrichtung ist nicht erforderlich. Eine bisher ausschließlich per API-Key geschützte Instanz bleibt für die Browser-Einrichtung gesperrt. Nach dem Upgrade einmalig den Dienst stoppen, `/opt/ludus/current/Ludus.Api account set` lokal ausführen und den Dienst wieder starten. Bestehende `X-Api-Key`-Integrationen arbeiten vor, während und nach diesem Übergang unverändert weiter. Die frühere Token- und Markerdatei werden nicht mehr verwendet und beim Start entfernt.

Globale Altprofile werden pro tatsächlich verwendeter Plattform verlustfrei dupliziert; Spiele werden dem passenden Klon zugeordnet und Custom-Format-Scores kopiert. Ein unbenutztes Profil bleibt erhalten und wird der Plattform mit der kleinsten ID zugeordnet. Nach dem Upgrade die Zuordnung dieses Profils prüfen und bei Bedarf über die Profilseite ändern. Vor dem Update wie üblich ein Backup erstellen.

### Natives Update

Den neuen, geprüften Checkout bereitstellen und denselben Installer erneut ausführen:

```bash
cd /root/Ludus
bash install-lxc.sh
```

Gespeicherte Pfade und Benutzer-IDs werden übernommen. Der Installer serialisiert parallele Aufrufe, baut zuerst ein neues Release und stoppt den Dienst erst für den Wechsel. Bei vorhandener Datenbank erstellt er vorher ein Offline-Archiv von `/config` ohne dessen `Backups` und `logs`; Mediencache, Recycling Bin und Import-Journale sind enthalten. Je nach Umfang ist dafür zusätzlich freier Speicher nötig.

Bei Fehlern vor der Aktivierung bleibt der bisherige Dienst aktiv beziehungsweise wird wieder gestartet. Scheitert die neue Aktivierung, wird der neue Dienst gestoppt; Release und Wiederherstellungsarchiv bleiben erhalten. Es gibt keinen automatischen Code-Downgrade nach möglichen Datenbankmigrationen. Ältere Releases und Offline-Archive werden nicht automatisch entfernt.

Für eine manuelle Wiederherstellung zunächst `systemctl stop ludus` ausführen. Den aktuellen Konfigurationsstand separat sichern, das gewünschte Offline-Archiv in ein neues leeres Verzeichnis entpacken und prüfen. Erst dann den Konfigurations-Mount und den `current`-Symlink auf einen zusammengehörigen Stand zurücksetzen; die alte Konfiguration nicht über eine bestehende WAL-Datenbank entpacken. Dateibesitzer und Schlüsselrechte wiederherstellen. Bei einem bind-gemounteten `/config` den Mount-Inhalt gezielt austauschen statt den Mountpoint umzubenennen. ROM-Veränderungen benötigen gegebenenfalls eine separate Speichersicherung.

## Fehlerdiagnose

| Symptom | Prüfung |
| --- | --- |
| Oberfläche nicht erreichbar | Bind-Adresse, Portfreigabe/Firewall, `systemctl status ludus` oder `docker compose ps` |
| `226/NAMESPACE` im LXC | Container-/AppArmor- und systemd-Mount-Berechtigungen prüfen; Sandbox nicht pauschal deaktivieren |
| `RootNotWritable` | Pfad aus Sicht des Dienstes, UID/GID-Abbildung, ACLs und systemd `ReadWritePaths` |
| `RemotePathMappingMissing` | Client-Host und Pfadpräfix, tatsächliche lokale Einbindung, Inhalt unterhalb des Download-Roots |
| Import kopiert statt zu verlinken | Gemeinsamer Mount, gleiche Dateisysteme, Quell-Schreibrechte und Hardlink-Unterstützung |
| Kein Suchtreffer | Provider-System-ID, überwachte Plattform/Spiel, Indexer-Kategorien, gespeicherte Credentials und Test-Buttons |
| Release ist abgelehnt | In der interaktiven Suche rotes Badge öffnen: Region, Status, Format, Mindestscore, Sperre oder Cutoff prüfen |
| `SubmissionNeedsReconciliation` | Nach unterbrochenem Submit den Client-Auftrag und dessen Ludus-Token prüfen; keinen zweiten Auftrag blind starten |
| Wiederholte Anmeldung/CSRF-Fehler | Browser-Cookies, Host-/HTTPS-Weiterleitung und tatsächliche Trusted Proxies prüfen |
| Download-Client nicht erreichbar | Container-DNS/Netzwerk, tatsächliche Basis-URL und separater Credential-Eintrag |
| Keyring-/Entschlüsselungsfehler | Richtigen `/config`-Mount einschließlich `keys` wiederherstellen, Schlüssel nicht neu generieren/löschen |

**System → Logs → Diagnose-ZIP** liefert bereinigte Logs und Systeminformationen ohne Datenbank/Keyring. Ein gewöhnliches Backup oder rohe Serverlogs sind kein Ersatz dafür. Rotierende Logs liegen unter `/config/logs/ludus.txt` und `ludus.debug.txt`.

## Entwicklung und Prüfungen

```bash
dotnet restore src/Ludus.sln --locked-mode
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-build --no-restore
npm --prefix frontend ci
npm --prefix frontend test
npm --prefix frontend run build
bash -n install-lxc.sh
bash tests/deployment/install-lxc-tests.sh
shellcheck install-lxc.sh tests/deployment/install-lxc-tests.sh
```

Entwicklungsstart und Playwright-Tests gegen eine isolierte Testinstanz sind in [Schritt 5](docs/step-5.md) beschrieben. Die Installer-Tests prüfen Eingabegrenzen, Dienstkonfiguration und Fehlerpfade mit ersetzten Hostbefehlen; sie installieren keine Pakete und starten keinen echten systemd-Dienst.

Projektstruktur:

```text
src/Ludus.Domain/             Entitäten und fachliche Typen
src/Ludus.Application/        Parser, Verifikation, Scoring und Verträge
src/Ludus.Infrastructure/     EF, Provider, Clients, Importe und Worker
src/Ludus.Api/                REST, Authentifizierung und SignalR
frontend/                    React-Oberfläche und Browser-Tests
tests/                       Backend- und Deployment-Tests
docker/                      Sicherer Container-Entrypoint
install-lxc.sh               Native Installation und Updates
docs/                        Architektur und Implementierungsdetails
```

Weitere Details: [Architektur](docs/architecture.md), [Parser/Scoring/Indexer](docs/step-2.md), [Download-Handling/Importe](docs/step-3.md), [API/Security/Backups](docs/step-4.md), [Frontend](docs/step-5.md), [Installer und Abschlussprüfung](docs/step-6.md).

Die Abschlussprüfung vom 27.11.2026 umfasst erfolgreiche Backend- und Frontend-Produktionsbuilds, 146 Backend-Tests, 12 Frontend-Tests und 8 Browser-Abläufe gegen eine isolierte Produktionsinstanz. Formatprüfung und EF-Modellabgleich sind erfolgreich; die vorhandenen 56 Installer-Prüfungen bleiben unverändert. Live-Integrationen brauchen eigene Credentials. Ein echter Proxmox-LXC-/Linux-systemd-Installationslauf und ein Docker-Image-Build wurden in der vorhandenen Windows-Entwicklungsumgebung nicht durchgeführt.
