# Ludus – Architekturstand nach Schritt 1

Die Ergänzungen aus Schritt 2 sind in [step-2.md](step-2.md) dokumentiert.
Downloads, Import, Worker und SignalR aus Schritt 3 sind in [step-3.md](step-3.md)
dokumentiert. Die folgenden Abschnitte beschreiben die ursprüngliche Basis aus Schritt 1.
API, Authentifizierung, Metadaten und Systemdienste aus Schritt 4 stehen in
[step-4.md](step-4.md).

Dieser Stand enthält die kompilierbare Betriebs- und Persistenzbasis. Er beschafft
noch keine Releases und bietet noch keine Verwaltungs-API. Die funktionsfähige
öffentliche Statusseite dient zur Prüfung der Asset-Pipeline. Die React-,
TypeScript- und Tailwind-Oberfläche mit den sechs Ansichten entsteht in Schritt 5.

## Projektstruktur und Abhängigkeiten

```text
Ludus/
├── .config/dotnet-tools.json
├── .env.example
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── NuGet.Config
├── Dockerfile
├── docker-compose.yml
├── docker-compose.hardlinks.yml
├── docker/entrypoint.sh
├── docs/architecture.md
├── frontend/
│   ├── package.json
│   ├── package-lock.json
│   ├── vite.config.js
│   ├── index.html
│   └── src/
│       ├── status.js
│       └── status.css
├── src/
│   ├── Ludus.sln
│   ├── Ludus.Domain/
│   │   ├── Common/
│   │   ├── Library/
│   │   ├── Verification/
│   │   ├── Profiles/
│   │   ├── Integrations/
│   │   ├── Activity/
│   │   ├── Configuration/
│   │   └── Operations/
│   ├── Ludus.Application/
│   │   └── Security/
│   ├── Ludus.Infrastructure/
│   │   ├── Configuration/
│   │   ├── Security/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   └── Migrations/
│   │   └── DependencyInjection.cs
│   └── Ludus.Api/
│       ├── Program.cs
│       └── appsettings.json
└── tests/Ludus.Infrastructure.Tests/
```

Jedes Backend-Projekt enthält seine eigene `.csproj` und `packages.lock.json`.
`wwwroot` wird beim Produktionsbuild aus `frontend/dist` erzeugt und nicht
eingecheckt. Es gibt keine Abhängigkeit der Domäne von EF, HTTP oder ASP.NET.

```mermaid
flowchart LR
    Browser --> API[Ludus.Api]
    API --> Infrastructure[Ludus.Infrastructure]
    Infrastructure --> Application[Ludus.Application]
    Application --> Domain[Ludus.Domain]
    Infrastructure --> SQLite[(SQLite /config/ludus.db)]
    Infrastructure --> Keys[/config/keys]
```

Die API ist Composition Root; Application enthält Anwendungsfälle und Ports,
Infrastructure implementiert technische Adapter. EF-Konfigurationen bleiben in
Infrastructure. Ein DbContext ist eine scoped Unit of Work. Ein generisches
Repository um EF wird nicht eingeführt. Neue Anwendungsfälle werden nach Feature
und nicht in einen globalen Service einsortiert.

Die folgenden Modulpfade werden mit ihrer Implementierung angelegt:

| Schritt | Modulpfade | Inhalt |
|---|---|---|
| 2 | `Application/Releases`, `Application/Verification`, `Application/Profiles`, `Infrastructure/Indexers` | Parser, DAT-Import, Scoring, Indexer |
| 3 | `Application/Downloads`, `Application/Imports`, `Infrastructure/DownloadClients`, `Infrastructure/Workers`, `Infrastructure/Notifications`, `Api/Hubs` | CDH, Import, Worker, SignalR |
| 4 | `Api/Features`, `Api/Security`, `Infrastructure/Metadata`, `Infrastructure/Library`, `Infrastructure/Operations` | Controller, Auth, Provider, Scanner, Backups |
| 5 | `frontend/src/features`, `frontend/src/api`, `frontend/src/locales`, `frontend/src/components` | React-Anwendung |
| 6 | `install-lxc.sh`, `README.md` | LXC-Installation und vollständiges Betriebshandbuch |

## Persistenzmodell

| Bereich | Tabellen und Beziehungen |
|---|---|
| Bibliothek | `Platforms`, `RootFolders`, `Games`, `GameFiles`, `GameMetadataReferences`, `GameMetadataTranslations` |
| DAT | `DatFiles` → `DatGames` → `DatRoms`; Dateien verweisen optional auf `DatRoms` |
| Qualität | `QualityProfiles`, `CustomFormats`, `CustomFormatRules`, M:N `QualityProfileFormats` mit profilabhängigem Score |
| Integration | `Indexers`, `DownloadClients`, `RemotePathMappings`, `MetadataProviders`, `MetadataCache`, `Webhooks` |
| Aktivität | `Downloads`, `ReleaseBlocks`, `History` |
| System | `AppSettings`, `SecuritySettings`, `ScheduledTasks`, `TaskRuns`, `HealthIssues`, `Backups` |

Spiele referenzieren genau eine Plattform, einen frei wählbaren Root Folder und
ein Qualitätsprofil derselben Plattform. Mehrere Profile pro Plattform sind möglich;
Custom Formats bleiben global wiederverwendbar und ihre Scores profilspezifisch. Root Folders sind keine Plattform-Unterobjekte. Sie dürfen
mehrere Plattformen enthalten; pro Plattform sind mehrere Roots möglich.
Dateipfade sind relativ zum Root Folder des Spiels. `FileGroupId` gruppiert
zusammengehörige ROMs, Deskriptoren, Tracks und Playlists. `ContentKind` trennt
Base, Update und Dlc; `Role` trennt ROM, Descriptor, Track, Playlist, Directory
und Support-Dateien. Revision, Disc-Nummer, Title-ID und Score bleiben pro Datei
erhalten. Ordnerbasierte Spiele können durch eine Directory-Datei und ihre
zugehörigen Dateien derselben Gruppe repräsentiert werden.

`Game.Status` ist ein von den späteren Anwendungsfällen gepflegter Zustand;
„Cutoff nicht erreicht“ und der Verifikationsfilter werden aus Profilen und
aktiven Dateien abgeleitet. Es gibt keinen separat zu pflegenden Cutoff-Status.
DAT-Dumps werden anhand ihrer Prüfsummen und Größe gesucht. CRC32 allein wird
nicht als sicherer Identitätsnachweis behandelt. `DatRom.Status` kann z. B.
`nodump` oder `baddump` aufnehmen; ein DAT-Treffer darf daher nicht automatisch
mit einem Good Dump gleichgesetzt werden. Die Abgleichlogik folgt in Schritt 2.

Root Folders, Plattformen, Profile und Download-Clients sind bei noch vorhandenen
Referenzen gegen Löschung geschützt. Beim Entfernen einer DAT bleibt die lokale
Datei erhalten; der DAT-Verweis wird NULL. History behält Titel und Event auch
nach Entfernung eines Spiels. Ein aktiver/verfolgter Download verhindert das
versehentliche Entfernen seines Spiels. Die späteren Lösch-Anwendungsfälle
müssen den gewünschten Umgang mit diesen Beziehungen ausdrücklich festlegen.

SQLite erzwingt Fremdschlüssel, eindeutige Indizes und CHECK-Constraints u. a.
für Hashlängen, Datei-Pfadtraversal, Cutoff, Intervalle, Singleton-Einstellungen,
Kontozustand/-Credentials, Enum-Werte und die unveränderliche Client-Kategorie `ludus`.
Listen sind JSON-Arrays mit Value Comparern für Änderungen innerhalb der Liste.
UTC-Zeitpunkte werden als `DateTime` gespeichert und beim Lesen mit UTC-Kind
versehen. Eine GUID als Concurrency Token schützt vor verlorenen Änderungen.

`ReleaseBlocks` unterscheidet temporäre Cooldowns mit Ablaufdatum von permanenten
Sperren ohne Ablaufdatum. Ein Release-Fingerprint identifiziert dasselbe Release
auch dann, wenn ein Indexer entfernt wird. Die Berechnung, sieben Tage Ablaufzeit
und Ersatzsuche werden in Schritt 3 implementiert.

## Start, Migration und Seed

1. Absolute Verzeichnispfade auflösen und ausschließlich Config-Verzeichnisse anlegen.
2. Exklusiven Prozess-Lock auf `/config/.ludus.lock` erwerben.
3. Data Protection mit festem App-Namen `Ludus` und persistentem Keyring konfigurieren.
4. SQLite mit Fremdschlüsseln und 30 Sekunden Busy-/Command-Timeout öffnen.
5. `PRAGMA journal_mode=WAL` aktivieren und Ergebnis prüfen.
6. Die eingecheckte EF-Migration mit `MigrateAsync` anwenden.
7. Erstinstallation in einer Transaktion initialisieren; danach HTTP bereitstellen.

Es wird kein `EnsureCreated` verwendet. Ein zweiter Prozess auf demselben
Config-Verzeichnis wird abgewiesen. Das Startlimit beträgt zwei Minuten; ein
fehlgeschlagener Start liefert keinen gesunden Healthcheck. `/config` muss auf
einem lokalen Dateisystem liegen, das SQLite-Dateisperren und WAL unterstützt,
nicht auf einer SMB-/NFS-Freigabe. Datenbank und Keyring sind eine gemeinsam zu
sichernde Einheit. Ohne Keyring können gespeicherte Secrets nicht entschlüsselt
werden; der Start bricht dann ab, statt unbemerkt Credentials zu ersetzen.

Der Seed enthält 22 Plattformen mit Endungen, Farben und passenden
Standardkategorien, einen Root `ROMs`, sechs Custom Formats, ein Profil
`Europe preferred` für die erste Plattform, zwei deaktivierte Metadatenprovider und sechs Task-Typen.
Er legt keine fiktiven Spiele, Downloads oder Zugangsdaten an. Provider-System-IDs
sind konfigurierbar und bleiben bis zur Provider-Konfiguration ungesetzt.
Roots werden registriert, aber nicht auf dem Dateisystem erzeugt.

Das Standardprofil bewertet Europe mit +100, USA mit +80, CHD mit +50 und
Beta/Demo/Proto jeweils mit −10000; Mindestscore 0, Cutoff 100, Upgrades erlaubt.
Diese Werte sind editierbar. Bestehende Einstellungen und gelöschte Seed-Einträge
werden bei Neustarts nicht überschrieben oder wieder angelegt. Neue
Katalogänderungen benötigen später eine bewusste Datenmigration.

## Sicherheitsbasis und spätere Schnittstellen

`ProtectedSecret` und `ISecretProtector` trennen Klartext von gespeicherten
Credentials. Die Felder für IGDB/ScreenScraper, Indexer, Clients, Webhooks und
Download-Quell-URLs speichern Data-Protection-Ciphertext. Webhook-URLs werden
vollständig verschlüsselt, da ihr Pfad ein Zugriffstoken enthalten kann.
HTTP-Basisadressen für Indexer/Clients dürfen weder Userinfo noch Query-String
enthalten; API-Keys gehören in separate Secret-Felder. Passwörter für Forms-Auth
und HTTP Basic werden als gemeinsamer Passwort-Hash modelliert, nicht als entschlüsselbares Secret.

Ein kryptografisch zufälliger API-Key mit 256 Bit Entropie wird beim ersten Start
erzeugt und verschlüsselt gespeichert. Er wird weder geloggt noch vom Status-
Endpoint ausgegeben. Die Authentifizierung und sichere Anzeige/Rotation des Keys
sind Bestandteil von Schritt 4. Es gibt derzeit keine ungeschützte CRUD-API.
Konfigurierte Proxy-Netze umgehen zukünftig keine Authentifizierung. Forwarded
Headers sind zunächst deaktiviert; die Liste startet nur mit Loopback.

Data Protection verschlüsselt die Secrets in SQLite, nicht automatisch den
Keyring selbst. Der Keyring ist über Unix-Modus 0700 und die Config-Zugriffsrechte
geschützt. Auf Windows gelten die ACLs des gewählten Config-Verzeichnisses.
Keyring, Datenbank, Credentials und vollständige Backup-Archive dürfen nicht in
den späteren Diagnostics-Export gelangen.

Der einzige anonyme Systemzustand ist `GET /api/v1/system/status`, ohne Credentials
exakt `{"status":"ok"}` und `Cache-Control: no-store`. Setup-, Login-, CSRF- und
signierte Medienrouten besitzen eng begrenzte anonyme Aufgaben. Nicht implementierte
API-Routen liefern 404, auch wenn der SPA-Fallback aktiv ist. Nur
`wwwroot` wird statisch ausgeliefert; `/config`, `/downloads` und `/roms` nicht.
Die Produktionspipeline kopiert Frontend-Assets vor `dotnet publish` in
`wwwroot`. Die API und die Assets teilen denselben Origin.

Logs rotieren größenbasiert: `ludus.txt` mit 10 MiB / 10 Dateien und
`ludus.debug.txt` mit 20 MiB / 5 Dateien. EF Sensitive-Data-Logging und
HTTP-Request-Logging sind nicht aktiviert. Der Diagnose-Export bereinigt Secrets,
Credential-Parameter, URLs und lokale Basispfade.

## Lokal bauen und prüfen

Benötigt werden .NET 10 SDK und Node >= 22.12. Die Paketversionen sind zentral
festgelegt; NuGet- und npm-Lockfiles werden eingecheckt.

```powershell
dotnet tool restore
dotnet restore src/Ludus.sln --locked-mode
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-restore
npm --prefix frontend ci
npm --prefix frontend run build
dotnet run --project src/Ludus.Api --no-launch-profile --urls http://127.0.0.1:8080
```

Unter Windows werden ohne Konfiguration `.local/config`, `.local/downloads`
und `.local/roms` relativ zum Prozess-Arbeitsverzeichnis verwendet. Unter Linux
sind die Defaults `/config`, `/downloads`, `/roms`. Explizit setzen lässt sich
dies mit `Ludus__ConfigDirectory`, `Ludus__DownloadDirectory` und
`Ludus__RomDirectory`; jeweils absolute Pfade verwenden.
Für die lokale Statusseite in einer zweiten Konsole `npm --prefix frontend run dev`
starten; Vite leitet `/api` an Port 8080 weiter.

Eine neue Datenbankmigration wird aus dem Workspace angelegt mit:

```powershell
dotnet tool run dotnet-ef migrations add ChangeName --project src/Ludus.Infrastructure --startup-project src/Ludus.Api --output-dir Persistence/Migrations
```

Die Design-Time-Factory startet keine API und verwendet eine vom produktiven
Config-Verzeichnis getrennte Verbindung. Beim Generieren einer Migration wird
keine Datenbank angelegt. Generierte Migrationen vor Verwendung prüfen.

## Containerbetrieb dieses Schritts

Vor dem Start `.env.example` nach `.env` kopieren und alle Bind-Mount-Quellen auf
dem Host anlegen. Compose erzeugt sie absichtlich nicht als root. Download- und
ROM-Verzeichnisse müssen bereits für PUID/PGID zugänglich sein. Für die
unveränderten Beispielwerte auf einem Linux-Host:

```bash
mkdir -p data/config data/downloads data/roms
cp .env.example .env
docker compose config
docker compose up -d --build
curl --fail http://127.0.0.1:8080/api/v1/system/status
```

Die hier verwendeten IDs 1000:1000 müssen zur Host-Konfiguration passen.
Der Entrypoint startet als root, validiert PUID/PGID und UMASK, korrigiert nur
`/config` und wechselt dann mit `setpriv` dauerhaft zur unprivilegierten UID/GID.
Der Besitzwechsel folgt keinen Symlinks und überschreitet keine Mount-Grenzen.
Die Anwendung erhält keine Linux-Capabilities. `/downloads` und `/roms` werden
weder angelegt noch rekursiv umgeschrieben. Ein expliziter Non-root-Containerstart
funktioniert mit bereits passenden Config-Rechten und identischen PUID/PGID.

Compose bindet Port 8080 standardmäßig nur an Loopback. Der Container besitzt
einen öffentlichen, credentialfreien Healthcheck mit zwei Minuten Startfrist.
Das Root-Dateisystem ist read-only; `/tmp` ist ein begrenztes tmpfs.

**Hardlinks:** Zwei getrennte Bind-Mounts `/downloads` und `/roms` können trotz
gemeinsamer Host-Platte Hardlinks und atomare Renames verhindern. Dafür liegt
eine zusätzliche Compose-Datei mit einem gemeinsamen `/data`-Mount bei. Sie
benötigt Docker Compose >= 2.24.4 wegen `!override`:

```bash
mkdir -p data/config data/media/downloads data/media/roms
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml config
docker compose -f docker-compose.yml -f docker-compose.hardlinks.yml up -d --build
```

Hier lauten die Pfade `/data/downloads` und `/data/roms`; der erste Root wird
entsprechend angelegt. Das ersetzt die Mountliste, statt zusätzliche widersprüchliche
Mounts zu ergänzen. Auch die Download-Clients sollten dieselben Pfade sehen.
Ein Wechsel bei existierender Datenbank verschiebt keine Dateien und ändert
keine bestehenden Roots. Der spätere Importer muss weiterhin EXDEV/fehlende
Hardlink-Unterstützung behandeln und bei Torrents die Seed-Quelle erhalten.

## Referenzen

- [Servarr Docker Guide](https://wiki.servarr.com/docker-guide), insbesondere gemeinsamer Mount, UID/GID und UMASK; [lesbare Quelldatei](https://raw.githubusercontent.com/Servarr/Wiki/master/docker-guide.md).
- [Prowlarr Standardkategorien](https://github.com/Prowlarr/Prowlarr/blob/develop/src/NzbDrone.Core/Indexers/NewznabStandardCategory.cs): 1010 ist NDS, 1020 PSP, 1030 Wii, 1060 WiiWare, 1080 PS3; keine erfundenen SNES-/Switch-Unterkategorien.
- [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview).
- [EF-Migrationen](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
- [SQLite-Einschränkungen](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).
- [Data-Protection-Konfiguration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
