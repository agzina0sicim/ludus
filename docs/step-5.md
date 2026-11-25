# Schritt 5 – React-Oberfläche

Die Oberfläche verwendet React, TypeScript, React Router, Vite, Tailwind CSS und den SignalR-JavaScript-Client. Sie arbeitet mit den APIs aus Schritt 4; es gibt keine produktiven Beispieldaten oder simulierten Aktionen. Die vorhandenen Docker-Build-Stages übernehmen `frontend/dist` nach `wwwroot`.

## Ansichten

| Route | Funktionen |
| --- | --- |
| `/` | Poster- und Tabellenansicht, Suchfeld, Plattform-/Root-Folder-/Status-/DAT-Filter, serverseitige Seitennavigation und gezielter Library-Scan |
| `/add` | Verzögerte Metadatensuche, IGDB/ScreenScraper/Plattform-Standard, manuelle Erfassung, Root Folder, Profil und Monitoring |
| `/games/:id` | Metadaten und Cover, Base/Update/DLC-Dateien, Hashes, DAT-Status, signierter ROM-Download, Verlauf und interaktive Suche mit aufklappbaren Ablehnungsgründen |
| `/activity` | Live-Warteschlange mit Fortschritt, Geschwindigkeit, ETA und Entfernen eines Auftrags |
| `/activity/history` | Paginierter Ereignisverlauf |
| `/activity/blocklist` | Permanente Sperren und Cooldowns einschließlich Entfernen |
| `/settings/media` | Root Folders, Naming-Schema, Hardlinks, Papierkorb, Archivgrenzen und Plattformen einschließlich Monitoring aller zugehörigen Spiele |
| `/settings/dat` | Plattformbezogener DAT-Upload, Aktivieren/Deaktivieren und Entfernen |
| `/settings/profiles` | Mindest-Score, Upgrade-Cutoff, Regionen, Ausschlüsse, Format-Gewichtungen und vollständiger Regel-Editor |
| `/settings/indexers` | Torznab/Newznab, Kategorien, Protokoll, Suchoptionen, Credentials und Verbindungstest |
| `/settings/clients` | qBittorrent/Transmission/SABnzbd, Kategorie `ludus`, Import-Tags, Auftragsbereinigung und Remote Path Mappings |
| `/settings/metadata` | Verschlüsselte Provider-Zugangsdaten, Cache-Dauer, Verbindungstest und System-ID-Liste; Plattform-Priorität unter Medienverwaltung |
| `/settings/connect` | Webhooks, Ereignisauswahl und Testnachricht |
| `/settings/general` | Allgemeine Einstellungen, Zeitpläne, Forms-Login, private Proxy-Netze sowie API-Key-Anzeige und -Reset |
| `/system` | Systeminformationen und Health-Warnungen |
| `/system/tasks` | Aufgaben aktivieren/deaktivieren, manuell starten und Ausführungsverlauf |
| `/system/backups` | Erstellen, herunterladen, validiert importieren, löschen und Wiederherstellung vorbereiten |
| `/system/logs` | Logdatei-/Level-/Textfilter, optionale Aktualisierung und bereinigtes Diagnose-ZIP |

Die Oberfläche ist deutsch/englisch übersetzbar. Die Schaltfläche `DE`/`EN` speichert die persönliche Browser-Auswahl. Ohne persönliche Auswahl gilt nach der Anmeldung die konfigurierte Standardsprache. Technische Werte wie Release-Namen, Provider-Bezeichnungen und Original-Logs werden unverändert angezeigt.

## Anmeldung und Secrets

- Beim ersten Start dient `/config/bootstrap-api-key` im Container als Zugang für die Browser-Anmeldung. Nativ befindet sich die Datei im konfigurierten Konfigurationsverzeichnis.
- Der Browser tauscht den eingegebenen API-Key oder Benutzername/Passwort gegen die bestehende HttpOnly-Cookie-Session aus. Er speichert keine Credentials in Local Storage oder Session Storage.
- Jeder schreibende API-Aufruf erhält einen CSRF-Header. Nach Anmeldung und Schlüsselwechsel wird der Token erneut bezogen. Fehlgeschlagene Schreibaktionen werden nicht automatisch wiederholt.
- Bei gespeicherten Secrets gibt es drei explizite Aktionen: unverändert lassen (`null`), ersetzen (neuer Wert) oder löschen (`""`). Der Server liefert ausschließlich die Konfiguriert-Flags zurück.
- Formular-Payloads enthalten nur die erlaubten Request-Felder. IDs, Versionsinformationen und andere gelesene Datenbankfelder werden nicht zurückgeschrieben.
- Cover und ROMs verwenden kurzlebige signierte URLs. Cover werden bei Bedarf geladen und ihre Links rechtzeitig erneuert. API-Keys werden nicht in Medien-URLs oder Bild-Tags eingesetzt.
- Sicherheitsänderungen widerrufen alle Sitzungen. Ein API-Key-Reset aktualisiert die aktuelle Browser-Sitzung mit dem neuen Key und verbindet SignalR erneut. Externe Anwendungen müssen ihren Key ebenfalls aktualisieren.
- Entfernen und Wiederherstellen verwenden Bestätigungsdialoge. Ein Webhook-Test sendet tatsächlich eine Nachricht; ein Verbindungstest nutzt die zuvor gespeicherte Konfiguration.

## Echtzeit und Fehlerbehandlung

`/hubs/activity` wird mit der Cookie-Session und einem CSRF-Header für die Aushandlung verbunden. Queue-, Import-, RSS-, Task-, Health- und Scan-Ereignisse aktualisieren die angezeigten Daten. Nach einer unterbrochenen Verbindung wird automatisch neu verbunden. Ergänzende Abfragen halten die Ansichten auch bei nicht verfügbarer Live-Verbindung aktuell.

Laufende Datenabfragen werden beim Wechsel der Ansicht oder Suchparameter abgebrochen. Späte Antworten können keine neueren Suchergebnisse überschreiben. Dialoge zeigen API-Fehler an und bleiben bei Fehlern geöffnet. Ein abgelaufener Login führt zurück zur Anmeldung.

Die responsive Sidebar besitzt Tastaturfokus-Begrenzung, Escape-Schließen und einen inaktiven Hintergrund auf kleinen Bildschirmen. Formular-Labels und Hinweise sind separat zugeordnet. Native Dialoge, sichtbare Fokusmarkierungen und reduzierte Animationen unterstützen die Tastaturbedienung.

## Lokale Entwicklung

Benötigt werden das .NET-10-SDK sowie Node.js 22.12 oder neuer; das Dockerfile verwendet Node 24.

Im Repository-Stamm das Backend starten:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.local/dotnet'
dotnet run --project src/Ludus.Api -c Release --no-launch-profile --urls http://127.0.0.1:8080
```

In einem zweiten Terminal:

```powershell
npm.cmd --prefix frontend ci
npm.cmd --prefix frontend run dev
```

Die Oberfläche ist standardmäßig unter `http://127.0.0.1:5173` erreichbar. Vite leitet `/api` und `/hubs` einschließlich WebSockets an Port 8080 weiter. Der ursprüngliche Host bleibt erhalten, damit die Origin- und CSRF-Prüfungen auch im Entwicklungsbetrieb greifen. Unter Linux entspricht `npm.cmd` dem Befehl `npm`.

Für eine lokale Prüfung des Produktions-Builds:

```powershell
npm.cmd --prefix frontend run build
$env:ASPNETCORE_WEBROOT = Join-Path $PWD 'frontend/dist'
dotnet run --project src/Ludus.Api -c Release --no-launch-profile --urls http://127.0.0.1:8080
```

Diese Variante liefert die Anwendung einschließlich SPA-Fallback und Security-Headern direkt aus ASP.NET Core aus. `vite preview` allein stellt keine Backend-API bereit.

## Prüfungen

```powershell
npm.cmd --prefix frontend run format:check
npm.cmd --prefix frontend test
npm.cmd --prefix frontend run build
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-build --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Ludus.Infrastructure --startup-project src/Ludus.Api --configuration Release --no-build
```

Die Frontend-Tests decken CSRF-Tokenwechsel, parallele Initialisierung, Session-Ablauf, nicht wiederholte Schreibaktionen, sichere Uploads/Medien-URLs, Secret-Payloads, Auswahlfeld-Labels, abgelehnte Releases und überholte Suchantworten ab.

Die Playwright-Tests verändern Konfiguration und erzeugen Testdaten. Ausschließlich gegen eine separate Testinstanz ausführen, niemals gegen eine produktive Bibliothek. Sie erstellen künstliche ROM-Testdateien ohne Spielinhalte sowie passende Prüfsummen.

Eine isolierte Instanz starten, bevor die Browser-Tests ausgeführt werden:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.local/dotnet'
$env:Ludus__ConfigDirectory = Join-Path $PWD '.local/frontend-qa/config'
$env:Ludus__DownloadDirectory = Join-Path $PWD '.local/frontend-qa/downloads'
$env:Ludus__RomDirectory = Join-Path $PWD '.local/frontend-qa/roms'
$env:ASPNETCORE_WEBROOT = Join-Path $PWD 'frontend/dist'
New-Item -ItemType Directory -Force -Path $env:Ludus__DownloadDirectory, $env:Ludus__RomDirectory | Out-Null
dotnet run --project src/Ludus.Api -c Release --no-build --no-launch-profile --urls http://127.0.0.1:8080
```

In einem zweiten Terminal im Repository-Stamm:

```powershell
$env:LUDUS_E2E_DIRECTORY = Join-Path $PWD '.local/frontend-qa'
$env:LUDUS_E2E_URL = 'http://127.0.0.1:8080'
$env:LUDUS_E2E_BROWSER = 'msedge'
npm.cmd --prefix frontend run test:e2e
```

Standardmäßig wird der lokal installierte Edge verwendet. Alternativ lässt sich mit `npx playwright install chromium` im Frontend-Ordner Chromium installieren und `LUDUS_E2E_BROWSER=chromium` setzen. Screenshots liegen unter `.local/frontend-e2e-results`; Traces, Videos und automatische Fehler-Screenshots sind wegen möglicher Credential-Felder ausgeschaltet.

Getestete Browser-Abläufe: Cookie-/Forms-Anmeldung, SignalR-Verbindung, Spielverwaltung, DAT-Import und Scan, Prüfsummen, signierter ROM-Download, sämtliche Ansichten, Custom Formats/Profil-Scores, allgemeine Einstellungen, DE/EN, mobile Navigation, Backup-Erstellung/-Download/-Import, Diagnose-Export und API-Key-Rotation.

Abschlussprüfung vom 24.11.2026: 12 Frontend-Tests, 4 Playwright-Tests und 132 Backend-Tests bestanden. TypeScript-/Vite-Build und Formatprüfung erfolgreich; .NET-Build ohne Warnungen und Fehler; keine ausstehenden EF-Modelländerungen. Der Bibliotheksablauf wurde zusätzlich über den Vite-Proxy geprüft. Desktop- und Mobilansichten wurden anhand echter Browser-Screenshots kontrolliert.

Externe Provider, Indexer, Download-Clients und Webhooks benötigen eigene Zugangsdaten. Die Browserprüfung führt keine echten externen Downloads oder Webhook-Nachrichten aus. Der Restore-Dialog nutzt den validierten Restore-Endpunkt aus Schritt 4; ein vollständiger Restore benötigt anschließend den Neustart durch Docker/systemd oder einen manuellen Start.

Schritt 6 (Proxmox-LXC-Installation und abschließende README) bleibt separat.
