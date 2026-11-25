# Schritt 6 – Proxmox-LXC und Betriebsdokumentation

`install-lxc.sh` installiert oder aktualisiert Ludus innerhalb eines bootfähigen Debian-12-/13-LXC mit systemd. Der Installer unterstützt amd64/arm64, lädt .NET 10 aus dem Microsoft-Paketfeed und prüft das offizielle Node-24-Archiv anhand seiner SHA256-Liste. Builds laufen unter `ludus-build`, der spätere Dienst unter `ludus`.

## Installation und Updates

- Eine bestehende Benutzer-ID wird nicht umnummeriert. Der Dienst kann eine vorhandene Mediengruppe verwenden.
- Nur der Konfigurationsbaum erhält rekursive Eigentümeränderungen. Bestehende Download- und ROM-Verzeichnisse werden auf Zugänglichkeit geprüft.
- Mit `--media-dir /data` liegen Downloads und ROMs unter einer gemeinsamen systemd-Schreibfreigabe. Separate schreibbare Bind Mounts könnten Hardlinks trotz gleicher Host-Platte verhindern.
- UID/GID und Speicherpfade werden in einer root-eigenen Datei mit Modus `0600` gespeichert und bei Updates übernommen. Die Datei wird als Schlüssel/Wert-Liste gelesen, niemals als Shellcode ausgeführt.
- Ein exklusives `flock` serialisiert Installationen. Ein neues Release wird vor dem Stoppen des Dienstes vollständig gebaut.
- Vor dem Wechsel einer vorhandenen Installation wird die gestoppte Konfiguration ohne Logs und Anwendungsbackups archiviert. Keyring, SQLite-WAL-Dateien und Import-Journale bleiben zusammen.
- Der Release-Symlink wird atomar ausgetauscht. Danach werden Dienststatus und der anonyme API-Healthcheck geprüft.
- Bei Fehlern vor der Aktivierung wird ein zuvor laufender Dienst wieder gestartet. Nach Beginn der Aktivierung bleibt der Dienst bei einem Fehler gestoppt; ein möglicherweise migrierter Datenbestand wird nicht automatisch mit einem älteren Release geöffnet.
- `Restart=always` ermöglicht auch den Neustart nach einem kontrollierten Restore-Exit. systemd begrenzt Schreibzugriff und Capabilities; individuelle Erweiterungen erfolgen über Drop-ins.

Die [README](../README.md) beschreibt Docker Compose, Proxmox-UID-Mapping und Bind Mounts, Installation, Ersteinrichtung, Prowlarr/Jackett, Download-Clients, Remote Path Mappings, Hardlink-Prüfung, Reverse Proxy, Backups, Updates und Fehlerdiagnose.

## Prüfung vom 25.11.2026

| Prüfung | Ergebnis |
| --- | --- |
| .NET Release-Build | Erfolgreich, keine Warnungen oder Fehler |
| Backend-Tests | 132 bestanden |
| TypeScript-/Vite-Produktionsbuild | Erfolgreich |
| Frontend-Tests | 12 bestanden |
| EF-Modell gegen letzte Migration | Keine ausstehenden Änderungen |
| Bash-Syntax und Installer-Hilfe | Erfolgreich |
| Installer-Tests | 56 Prüfungen bestanden |
| ShellCheck 0.11.0 | Keine Befunde |

Die Installer-Tests prüfen Eingabegrenzen, Pfadüberlappungen, persistierte Optionen, Shell-Injection-Abwehr und die erzeugte Dienstkonfiguration. Aktivierungstests ersetzen Hostbefehle und prüfen erfolgreichen Start, belegten Port, fehlgeschlagenen Healthcheck sowie Fehler bei Unit-Prüfung und Dienstaktivierung. Sie installieren keine Pakete und verändern weder `/etc` noch `/opt`.

Die Shellprüfungen liefen unter GNU Bash in der Windows-Entwicklungsumgebung. Ein tatsächlicher Linux-/Proxmox-Installationslauf, eine echte systemd-Unit-Aktivierung und ein Docker-Image-Build wurden hier nicht durchgeführt. Die vier erfolgreichen Browser-Abläufe aus Schritt 5 wurden für diese Änderungen an Installer und Dokumentation nicht erneut ausgeführt. Externe Clients und Provider benötigen eine separate Prüfung mit den jeweiligen Zugangsdaten.
