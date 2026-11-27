# Schritt 4 – API, Security, Metadaten und Systemdienste

Schritt 4 ergänzt die Verwaltungs-API und die Dienste für die in Schritt 5 geplante
React-Oberfläche. Alle REST-Routen liegen unter `/api/v1`. Der SignalR-Hub bleibt
unter `/hubs/activity`. Die öffentliche Statusseite aus Schritt 1 ist weiterhin
vorhanden; sie ist noch keine Verwaltungsoberfläche.

## Einstieg und Authentifizierung

Ohne Anmeldung liefert ausschließlich der Status-Endpunkt Systeminformationen
in der minimalen Form `{"status":"ok"}`. Die weiteren anonymen Routen sind
`auth/csrf`, `auth/session`, `auth/login`, `auth/setup` und der durch ein eigenes zeitbegrenztes
Token geschützte Medienabruf. Unbekannte API-Routen fallen nicht auf die SPA zurück.

Eine neue Instanz beginnt im Setup-Modus. `POST auth/setup` legt genau einmal
Benutzername, ASP.NET-Identity-Passworthash und die Methode `Forms` oder `Basic`
fest. `Forms` ist der Standard und verwendet das HttpOnly-Session-Cookie; `Basic`
antwortet bei fehlenden Zugangsdaten mit `WWW-Authenticate: Basic` und verwendet
dieselben Zugangsdaten. Im Forms-Modus wird diese Challenge bewusst nicht gesendet.

Der separat generierte API-Key wird verschlüsselt in SQLite gespeichert. Externe
Dashboards und Integrationen verwenden `X-Api-Key`. Ein ausdrücklich übermittelter ungültiger
Key kann nicht durch ein zusätzliches gültiges Cookie ersetzt werden. API-Keys
werden nicht aus Query-Strings gelesen und sind keine Browser-Anmeldemethode.

Eine migrierte Instanz ohne Benutzerkonto beginnt in `LegacyProtected`. Beim Start
wird lokal `/config/account-setup-token` mit Modus `0600` unter Unix erzeugt; das
Setup akzeptiert nur dessen SHA256-verifizierten Einmalcode. Vorhandene Benutzernamen
und Hashes wechseln direkt nach `Complete`. Für eine lokale Wiederherstellung wird
bei gestopptem oder laufendem Dienst der Marker `/config/reset-account` angelegt und
Ludus neu gestartet; der Marker wird verbraucht und ein neuer Einmalcode erzeugt.

Der Browser-Anmeldeablauf lautet:

1. `GET /api/v1/auth/csrf` abrufen; Cookie behalten und den zurückgegebenen `token`
   als Header `X-CSRF-Token` senden.
2. Im Forms-Modus `POST /api/v1/auth/login` mit `username`/`password` senden. Im
   Basic-Modus übernimmt der Browser die `Authorization: Basic`-Aushandlung.
3. Nach erfolgreicher Anmeldung den CSRF-Token erneut abrufen, da er jetzt an den
   authentifizierten Benutzer gebunden ist.
4. Bei schreibenden Cookie-Anfragen einschließlich Logout und SignalR-Negotiation
   den CSRF-Header mitsenden. API-Key-Anfragen benötigen diesen Header nicht.

Cookies sind `HttpOnly`, `SameSite=Strict` und bei HTTPS `Secure`; Sitzungen gelten
zwölf Stunden ohne gleitende Verlängerung. Login ist auf zehn Versuche je Minute
und Client-IP begrenzt. Forms-Passwörter werden mit ASP.NET Identity gehasht;
neue Passwörter müssen mindestens zwölf Zeichen enthalten. Der Login-Bildschirm
wird mit dem Frontend in Schritt 5 umgesetzt.

`PUT auth/security` konfiguriert Forms-/Basic-Zugang und Reverse Proxies. Sicherheitsänderungen
und `POST auth/api-key/reset` ändern die Session-Version und widerrufen bestehende
Cookies und Medienlinks. SignalR sendet anschließend nur noch an die aktuelle
Session-Gruppe. Der neue Key wird beim Reset einmal in der authentifizierten
Antwort zurückgegeben; die alte Bootstrap-Datei wird entfernt.

## Reverse Proxies und Medien

Die Forwarded-Header-Verarbeitung ist standardmäßig ausgeschaltet. Konfigurierbare
Trusted Networks müssen vollständig innerhalb von RFC1918-Netzen oder Loopback
liegen; `0.0.0.0/0`, öffentliche Netze und übergroße Teilnetze werden abgelehnt.
`X-Forwarded-For` und `X-Forwarded-Proto` werden mit begrenzter Hop-Anzahl und
passenden Header-Anzahlen verarbeitet. Der Proxy muss den ursprünglichen `Host`
weitergeben; `X-Forwarded-Host` wird nicht übernommen. Proxy-Vertrauen ersetzt
keine Authentifizierung. Siehe die
[ASP.NET-Core-Dokumentation zu Forwarded Headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

`POST media/links` nimmt entweder `{"kind":"rom","fileId":123}` oder
`{"kind":"cover","coverRelativePath":"cover.png"}` entgegen. Die Antwort enthält
`url` und `expiresAtUtc`. Das Data-Protection-Token bindet Ressource, Dateiversion,
Session-Version und Ablaufzeit. Der Standard beträgt fünf Minuten; konfigurierbar
sind 30 bis 3600 Sekunden. ROM-Abrufe unterstützen Range-Requests. Traversal und
Symlinks werden vor dem Öffnen abgewiesen. Cover werden ausschließlich als PNG,
JPEG oder WebP ausgeliefert. In den URLs steht niemals der API-Key.

Die API liefert `Cache-Control: no-store`; Medienabrufe zusätzlich eine private
Cache-Direktive. `Referrer-Policy: no-referrer`, `nosniff` und die bestehende CSP
bleiben aktiv. Fremde Browser-Origins werden am SignalR-Hub abgewiesen. Native
Clients können weiterhin einen API-Key im Header verwenden; Browser-WebSockets
verwenden das Session-Cookie.

## REST-Schnittstellen

JSON verwendet camelCase und Enum-Namen wie `Igdb`, `ScreenScraper`, `QBittorrent`
oder `VerifiedGoodDump`. Unbekannte JSON-Felder werden abgewiesen. Schreibmodelle
enthalten ausdrücklich freigegebene Felder; EF-Navigationen, IDs, Ciphertexte und
interne Zustände werden nicht aus Request-Objekten übernommen.

| Bereich | Routen relativ zu `/api/v1` |
|---|---|
| Authentifizierung | `GET auth/csrf`, `GET auth/session`, `POST auth/setup`, `POST auth/login`, `POST auth/logout`, `GET/PUT auth/security`, `GET auth/api-key`, `POST auth/api-key/reset` |
| Bibliothek | `GET/POST games`, `GET/PUT/DELETE games/{id}`, `GET games/calendar`, `PUT games/{id}/metadata`, `POST games/{id}/refresh-metadata` |
| Suche/Grab | `GET games/{id}/search`, `POST games/{id}/grab` |
| Root Folders | `GET/POST root-folders`, `GET/PUT/DELETE root-folders/{id}` |
| Plattformen | `GET/POST platforms`, `GET/PUT/DELETE platforms/{id}`, `PUT platforms/{id}/monitoring` |
| DAT | `GET/POST dat-files`, `GET/PUT/DELETE dat-files/{id}` |
| Profile | `GET/POST profiles`, `GET/PUT/DELETE profiles/{id}` |
| Custom Formats | `GET/POST custom-formats`, `GET/PUT/DELETE custom-formats/{id}` |
| Indexer | `GET/POST indexers`, `GET/PUT/DELETE indexers/{id}`, `POST indexers/{id}/test` |
| Download-Clients | `GET/POST download-clients`, `GET/PUT/DELETE download-clients/{id}`, `POST download-clients/{id}/test` |
| Pfadübersetzung | `GET/POST remote-path-mappings`, `GET/PUT/DELETE remote-path-mappings/{id}` |
| Metadaten | `GET metadata/search`, `GET metadata/{provider}/systems`, `POST metadata/{provider}/test`, `GET metadata/providers`, `PUT metadata/providers/{provider}` |
| Connect | `GET/POST webhooks`, `GET/PUT/DELETE webhooks/{id}`, `POST webhooks/{id}/test` |
| Einstellungen | `GET/PUT settings` |
| Aktivität | `GET activity/queue`, `POST activity/queue/{id}/import`, `GET activity/history`, `GET activity/blocklist`, `DELETE activity/blocklist/{id}`, `DELETE activity/queue/{id}` |
| Medien | `POST media/links`, `GET media/content?token=...` |
| System | `GET system/status`, `GET system/info`, `GET system/health`, `POST system/health/check` |
| Aufgaben | `GET system/tasks`, `PUT system/tasks/{kind}`, `POST system/tasks/{kind}/run`, `POST system/library/scan` |
| Backups | `GET/POST system/backups`, `POST system/backups/import`, `GET system/backups/{id}/download`, `POST system/backups/{id}/restore`, `DELETE system/backups/{id}` |
| Diagnostik | `GET system/logs`, `GET system/diagnostics` |

Die vollständigen Eingabeverträge mit Längen-, Bereichs- und Pflichtfeldvalidierung
stehen als Request-Records in `src/Ludus.Api/Features`. Für Integration-Secrets gilt:
`null`/nicht angegeben behält den bisherigen Wert, eine leere Zeichenfolge entfernt
ihn, ein neuer Wert wird verschlüsselt gespeichert. Erforderliche URLs dürfen nicht
entfernt werden. Antworten liefern ausschließlich `...Configured`-Flags.

Weitere Details:

- `games` akzeptiert `query`, `platformId`, `rootFolderId`, `verification`, `page`
  und `pageSize` sowie `status=missing|downloading|available|cutoffUnmet|unmonitored|all`.
  Seiten enthalten höchstens 200 Einträge. History unterstützt `gameId` und dieselbe
  Seitengröße.
- Ein neues Spiel kann manuell mit Titel oder mit Provider/External-ID angelegt
  werden. Ein nachträgliches Metadaten-Matching aktualisiert keine Dateipfade.
- Die interaktive Suche behält abgelehnte Releases samt Rejection Reasons und
  vergibt zehn Minuten gültige serverseitige Grab-Tickets. Der Browser sendet beim
  Grab nur `ticket`; Download-URLs mit Indexer-Credentials gelangen nicht ins JSON.
  Ein Neustart macht alte Such-Tickets ungültig.
- DAT-Uploads sind Multipart-Anfragen mit `platformId` und `file`; die Grenze
  beträgt 256 MiB. Die vorhandene sichere XML-Pipeline wird wiederverwendet.
- `PUT platforms/{id}/monitoring` setzt Plattform und alle zugehörigen Spiele.
- Profilantworten enthalten `profile` und `formats`; jedes Profil besitzt genau eine
  Plattform. Spiele akzeptieren nur Profile derselben Plattform. Formatantworten
  enthalten ihre wiederverwendbaren Regeln; Scores bleiben profilspezifisch.
  Ungültige reguläre Ausdrücke werden bereits beim Speichern abgewiesen.
- Root-Verzeichnisse müssen vorhanden sein. Ein Root-Wechsel bei registrierten
  Dateien wird nicht als implizite Dateiverschiebung ausgeführt. Ein Scan kann
  tatsächlich verschobene Dateien wieder zuordnen.
- Das Entfernen eines Spiels lässt seine Dateien bestehen. Aktive Downloads oder
  ausstehende Client-Bereinigung verhindern das Entfernen. Das Entfernen eines
  Queue-Eintrags erhält ebenfalls die Client-Daten.
- Manuelle Task-Anforderungen liegen bis zur Ausführung in einer begrenzten
  In-Memory-Warteschlange je Task-Typ. Sie aktivieren keine deaktivierten Zeitpläne.
  Nach einem Neustart müssen noch nicht gestartete manuelle Anforderungen erneut
  gestellt werden; gestartete Läufe werden in `TaskRuns` protokolliert.

Fehler antworten mit HTTP-Status und einem bereinigten Fehlercode: beispielsweise
400 für ungültige Eingaben, 401 für fehlende Authentifizierung, 409 für Konflikte,
429 für Login-Limits, 502 für Provider-/Clientfehler und 503 für Speicherprobleme.
Externe Antworttexte und Zugangsdaten werden nicht als Exception-Details ausgeliefert.

## Metadatenprovider und Cache

`IMetadataProvider` bietet Suche, Detailabruf, Systemliste und Verbindungstest.
IGDB verwendet Twitch Client Credentials, speichert Access Tokens verschlüsselt,
erneuert sie vor Ablauf und versucht nach einem 401 einmal eine neue Anmeldung.
Die Suche ist auf die konfigurierte IGDB-Plattform begrenzt. API-Aufrufe werden
serialisiert und mit mindestens 260 ms Abstand ausgeführt. Grundlage sind die
[offiziellen IGDB-Dokumente](https://api-docs.igdb.com/).

ScreenScraper v2 verwendet Entwickler-ID/-Passwort sowie optional Benutzername und
Passwort. `jeuRecherche.php`, `jeuInfos.php`, `systemesListe.php` und
`ssuserInfos.php` liefern Suche, Details, Systemzuordnung und Credential-Test.
Titel werden nach Region, Beschreibungen und Genres nach Sprache gelesen. Siehe die
[offizielle ScreenScraper-API](https://www.screenscraper.fr/webapi2.php).

Die bevorzugte Metadatensprache ist unabhängig von der UI-Sprache. IGDB-
`game_localizations` liefern regionsbezogene lokalisierte Namen, aber keine
entsprechend lokalisierten Beschreibungen; ScreenScraper liefert regiongebundene
Namen und sprachgebundene `synopsis_*`-/Genre-Felder. Titel und Beschreibung tragen
deshalb getrennte Sprachkennzeichen. Verfügbare Fassungen werden je Spiel gespeichert,
der Cache-Schlüssel enthält die gewünschte Sprache, und ein Refresh ersetzt nur die
Fassungen des aktualisierten Providers. Feldweiser Fallback erhält die tatsächlich
angezeigte Sprache.

Plattform-IDs werden über die jeweilige Systemliste explizit zugeordnet. Es werden
keine geratenen IDs eingetragen. Ohne gewählten Provider verwendet die Suche die
Plattformpriorität und bei Bedarf den aktivierten Fallback. Leere Ergebnisse eines
erreichbaren Providers bleiben auch bei nicht verfügbarem Fallback eine leere Suche.

Der Cache liegt in SQLite, standardmäßig für 168 Stunden. Das explizite
Refresh-Kommando umgeht den Detailcache. Cover liegen unter `/config/Media` und
werden anhand ihres Inhalts adressiert. Nur HTTPS-Bilder von IGDB beziehungsweise
ScreenScraper werden abgerufen; Redirects werden nicht automatisch verfolgt.
Bildformat und Größe werden geprüft. Remote-Medien-URLs werden vor dem Speichern
aus den Cache-Objekten entfernt, damit ScreenScraper-Zugangsdaten nicht im Cache
oder im Browser landen. Ein fehlendes Cover wird mit einem bereinigten Code geloggt,
ohne die übrigen Metadaten zu verwerfen.

## Self-Healing Library Scan

Der periodische Scan und `POST system/library/scan` benutzen denselben Scanner.
Der direkte Aufruf akzeptiert `rootFolderId` und `platformId`; beide sind optional.
Bei mehrdeutigen Endungen wie `.iso` ist eine explizite Plattform sinnvoll.
Unbekannte Dateien werden nur bei eindeutiger Plattformzuordnung aufgenommen.

Der Scanner berechnet CRC32, MD5 und SHA1 und prüft aktivierte DATs. Er erkennt
geänderte Dateien sowie fehlende Dateien. Verschiebungen innerhalb eines Roots
werden über SHA1 und Größe repariert. Zwischen Roots wird ein bestehendes Spiel
nur dann umgehängt, wenn sein vollständiger Dateisatz eindeutig im neuen Root
gefunden wird und alle bisherigen Dateien fehlen. Der alte Root muss dabei
erreichbar sein; ein nicht eingebundener Datenträger wird nicht als bestätigte
Verschiebung interpretiert. Mehrdeutige Treffer erzeugen keine automatische Umhängung.

CUE/GDI-Referenzen und PS3-/Wii-U-Ordnerstrukturen bleiben zusammengehörig.
Neue Spiele erhalten das erste Qualitätsprofil; bereits bekannte Spiele behalten
ihre Zuordnung. Updates/DLCs werden anhand der vorhandenen Identität zugeordnet;
ein unbekanntes DLC erzeugt kein neues Basisspiel. Scans verändern keine ROM-Inhalte
und nehmen keine Umbenennungen vor. Der Import-Gate verhindert gleichzeitige
Dateiveränderungen durch den CDH-Importer. SignalR sendet `scanUpdated`.

## Health, Backups und Restore

Health prüft Schreibbarkeit der Roots, aktive Clients, RSS-Indexer, Provider-Zugänge,
System-ID-Zuordnungen und bekannte Remote-Pfade. Ergebnisse stehen in `HealthIssues`
und unter `system/health`; behobene Warnungen verschwinden beim nächsten Durchlauf.
SignalR sendet `healthUpdated`. Die vorhandene Zeitplanung führt jetzt alle sechs
Task-Typen aus, einschließlich Library Scan, Health Check und Backup.

Backups verwenden die SQLite Online Backup API, sodass committed WAL-Daten im
Snapshot enthalten sind. ZIPs enthalten `ludus.db`, Data-Protection-Keyring und ein
Manifest mit Schema- und SHA256-Angaben. Konfiguration, verschlüsselte Credentials
und Profile liegen in der Datenbank. ROMs, Download-Daten, Mediencache, Logs und
Bootstrap-Key sind nicht enthalten. Geplante Sicherungen liegen unter
`/config/Backups/scheduled`, manuelle/importierte unter `Backups/manual`. Retention
gilt nur für geplante Sicherungen. Offene Import-Journale blockieren eine neue
Sicherung, bis ihr konsistenter Abschluss geklärt ist.

Ein vorhandenes ZIP kann als roher Request-Body mit `Content-Type: application/zip`
an `system/backups/import` übertragen werden, auch auf einer neuen Installation.
Die Upload-Grenze beträgt 9 GiB; ein Reverse Proxy kann eine kleinere Grenze setzen.
Der Import registriert das Archiv und prüft sein Manifest. Vor einem Restore folgen
zusätzlich vollständige Hash-, XML-, SQLite-Integritäts-, Fremdschlüssel-, Schema-
und Keyring-Entschlüsselungsprüfungen. Nicht unterstützte Schlüssel-Deserializer,
maschinengebundene verschlüsselte Keyrings, Pfadtraversal und unerwartete Einträge
werden abgewiesen. Die von Ludus standardmäßig erzeugten Keyrings sind unterstützt.

`POST system/backups/{id}/restore` bereitet den Restore vor und antwortet mit 202.
Danach beendet sich der Host kontrolliert. Beim nächsten Start wird der vorbereitete
Stand unter dem exklusiven Instanz-Lock angewendet, bevor Migrationen, Secrets und
Worker verwendet werden. Docker Compose startet den Dienst mit seiner bestehenden
Restart-Policy erneut; ein lokal mit `dotnet run` gestarteter Prozess muss erneut
gestartet werden. Der LXC-Service entsteht in Schritt 6.

Der vorherige Datenbankstand und vorhandene Import-Journale bleiben unter
`/config/restore-previous/<id>` erhalten. Bestehende Keyring-Dateien werden erhalten
und geprüfte fehlende Schlüssel ergänzt. Alte Cookie-/Medien-Sitzungen werden
widerrufen. Ein wiederholter Start kann einen noch markierten Restore erneut
anwenden. ROMs werden durch den Restore nicht zurückgesetzt; nach Wiederherstellung
sollte die Bibliothek gescannt und die Client-Queue geprüft werden. Frühere
Staging-Verzeichnisse bleiben für eine mögliche manuelle Wiederherstellung erhalten.

Backups enthalten sowohl Datenbank als auch Keyring und sind deshalb vertraulich.
Unter Linux erhalten Archive Modus `0600`, Arbeits-/Wiederherstellungsverzeichnisse
Modus `0700`. Download und Restore erfordern authentifizierte API-Anfragen.

## Logs und Diagnostics

`system/logs` akzeptiert `debug`, `level=INF|DBG|WRN|ERR|FTL` und `limit` bis 2000.
Der Viewer und das Diagnostics-ZIP verwenden dieselbe Bereinigung: bekannte
Credentials/Ciphertexte, kodierte Varianten, credentialtypische Zuweisungen und
URLs werden ersetzt. Standard-Speicherpfade werden normalisiert. Der Export enthält
nur zwei bereinigte Log-Ausschnitte und `system.json` mit Versionen, Zählwerten,
Migration und Health-Codes. Datenbank, Keyring, Konfigurations-JSON und Backup-ZIPs
werden nicht in Diagnostics aufgenommen.

## Geprüfter Umfang

Die Tests verwenden echte temporäre SQLite-Datenbanken und Dateisysteme sowie
ASP.NET-Testhosts. Provider-Anfragen verwenden kontrollierte HTTP-Antworten. Geprüft
sind unter anderem Cookie/CSRF/API-Key-Priorität, Login-Limit, private Proxies,
SignalR-Origin, Token-Ablauf und -Widerruf, Range-Downloads, Secret-Verschlüsselung,
API-Workflows, OAuth-Erneuerung und Provider-Fallback, lokaler Covercache,
DAT-Scan, Verschiebungen innerhalb und zwischen Roots, Backup-Retention, Restore
einschließlich unabhängiger Installation, Archiv-Traversal und Diagnostics-Bereinigung.

Live-Aufrufe mit echten IGDB-/ScreenScraper-Zugangsdaten und ein Linux-/Docker-
Integrationstest sind hier nicht erfolgt. Die vollständige React-Oberfläche bleibt
Schritt 5; das Betriebshandbuch und LXC-Skript bleiben Schritt 6.

```powershell
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-restore
npm --prefix frontend run build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Ludus.Infrastructure --startup-project src/Ludus.Api --configuration Release --no-build
```

Das Datenmodell aus Schritt 3 deckt die neuen Dienste bereits ab; Schritt 4 benötigt
keine zusätzliche EF-Migration.
