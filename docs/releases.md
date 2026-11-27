# Ludus-Builds für GitHub

## Dateien und Voraussetzungen

Ein Release enthält drei Archive und `SHA256SUMS`:

| Datei | Inhalt und Verwendung |
| --- | --- |
| `ludus-0.1.0-source.zip` | Quellcode einschließlich versteckter GitHub-/Tool-Konfiguration. Entpackten Inhalt in das GitHub-Repository übernehmen. |
| `ludus-0.1.0-portable.tar.gz` | Kompiliertes Backend, React-Frontend und native SQLite-Abhängigkeiten; für Linux-Releases. |
| `ludus-0.1.0-portable.zip` | Derselbe kompilierte Stand als ZIP. |
| `SHA256SUMS` | SHA256-Prüfsummen aller drei Archive. |

Der portable Build benötigt die **ASP.NET-Core-Runtime 10**, einschließlich .NET Runtime, auf dem Zielsystem. Er enthält keinen eigenen .NET-Laufzeitinstaller. Linux-x64 und Linux-arm64 enthalten die benötigten SQLite-Bibliotheken. Der Build wird auf dem ausführenden Betriebssystem mit einer isolierten Datenbank gestartet und auf Healthcheck, Frontend und JavaScript-Auslieferung geprüft. Das ersetzt keine Prüfung jeder Zielarchitektur. Grundlagen: [Microsoft: .NET-Veröffentlichung](https://learn.microsoft.com/en-us/dotnet/core/deploying/).

Lokale Konfiguration, Datenbanken, Data-Protection-Schlüssel, ROMs, Downloads und Build-Caches gehören nicht in die Archive. Für den Quellcode wird eine festgelegte Auswahl von Projektdateien kopiert; Geheimnisse dürfen auch nicht direkt in diesen Quellcode geschrieben werden. Build-/Test-Arbeitsverzeichnisse bleiben ausschließlich unter dem ignorierten `.local`-Verzeichnis.

## Lokal erstellen

Voraussetzungen: PowerShell 7.2+, .NET SDK 10, Node 24, npm und `tar`. Im Repository-Stamm:

```powershell
pwsh -File scripts/build-release.ps1 -Version 0.1.0
```

Der Standardlauf installiert die npm-Abhängigkeiten über `npm ci`, stellt NuGet-Pakete mit Lockdateien wieder her, führt Frontend-/Backend-Tests und Builds aus, veröffentlicht die Anwendung und startet einen isolierten Funktionstest. Erst danach werden die Archive nach `artifacts/releases/v0.1.0` verschoben. Ein bereits existierendes Release-Verzeichnis wird nicht überschrieben.

Bei bereits wiederhergestellten Abhängigkeiten kann lokal `-UseExistingDependencies` ergänzt werden. Tests und Builds werden damit weiterhin ausgeführt. Für GitHub-Builds wird immer der vollständige Standardlauf verwendet.

## Auf GitHub hochladen

1. Ein Repository für Ludus anlegen.
2. Den **Inhalt** des Quellcode-ZIPs in das Repository übernehmen, einschließlich `.github`, `.config` und der anderen Punktdateien. Nicht lediglich die ZIP-Datei als Repository-Inhalt hochladen. Git beziehungsweise GitHub Desktop erhält die vollständige Ordnerstruktur zuverlässiger als eine manuelle Browser-Auswahl.
3. Auf `main` oder `master` pushen. Unter **Actions → Build Ludus** läuft der Build automatisch. Pull Requests und Tags mit Präfix `v` lösen ihn ebenfalls aus.
4. Nach erfolgreichem Lauf unter **Artifacts** das Paket `ludus-release-…` herunterladen. Alternativ **Run workflow** verwenden und die gewünschte Versionsnummer eintragen.
5. Unter **Releases → Draft a new release** beispielsweise den Tag `v0.1.0` wählen/anlegen. Die beiden portablen Archive und `SHA256SUMS` als Assets anhängen; bei Bedarf zusätzlich das Quellcode-ZIP. Anschließend den Release veröffentlichen.

Der Workflow besitzt nur Leserechte für Repository-Inhalte. Er lädt Build-Artefakte hoch, veröffentlicht aber nicht selbstständig einen öffentlichen GitHub-Release. Die Checks laufen auf einem Ubuntu-Runner. Anleitung zu Builds und Artefakten: [GitHub Actions für .NET](https://docs.github.com/en/actions/tutorials/build-and-test-code/net).

## Installation in einem Debian-LXC

Für die automatische Einrichtung von Benutzer, Pfaden und systemd weiterhin **den Quellcode** und `install-lxc.sh` verwenden, wie in der Projekt-README beschrieben. Nach dem Hochladen kann der Repository-Inhalt direkt über Git bezogen werden. Der Installer baut den gewählten Quellstand selbst; er nimmt kein portables ZIP als Eingabe an.

Ein portables Archiv eignet sich für einen bereits eingerichteten Dienst oder einen manuellen Start. In einem neuen, leeren Anwendungsverzeichnis entpacken, die ASP.NET-Core-Runtime 10 bereitstellen und als Benutzer mit Zugriff auf die angegebenen Datenpfade starten:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://0.0.0.0:8080
export Ludus__ConfigDirectory=/config
export Ludus__DownloadDirectory=/data/downloads
export Ludus__RomDirectory=/data/roms
dotnet Ludus.Api.dll
```

Die Datenpfade müssen vor dem Start passend bereitgestellt werden. Der Vorgang ersetzt nicht die systemd-Einrichtung. Beim Aktualisieren eines bestehenden Dienstes zuerst ein Backup erstellen und den Dienst stoppen; Datenbank und Keyring bleiben außerhalb des Anwendungsverzeichnisses. Ein älteres Release darf nicht ungeprüft gegen eine bereits migrierte Datenbank gestartet werden.

Vorhandene Konten behalten Benutzername und Password-Hash. Bei einer älteren API-Key-only-Datenbank oder zur lokalen Kontowiederherstellung wird das Konto bei gestopptem Dienst interaktiv gesetzt:

```bash
systemctl stop ludus
/opt/ludus/current/Ludus.Api account set
systemctl start ludus
```

Bei einem portablen Archiv mit korrekt gesetzten `Ludus__*`-Umgebungsvariablen stattdessen `dotnet Ludus.Api.dll account set` ausführen. Der Browser bietet für eine API-Key-only-Altinstanz kein Setup an; vorhandene `X-Api-Key`-Integrationen bleiben aktiv.

Unter Linux können heruntergeladene Archive mit `sha256sum --check SHA256SUMS` geprüft werden, wenn alle drei Archive daneben liegen. Unter PowerShell liefert `Get-FileHash -Algorithm SHA256` die Vergleichswerte.
