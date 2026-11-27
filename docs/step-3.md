# Schritt 3 – Downloads, Import und Hintergrundverarbeitung

Dieser Stand implementiert die Download- und Importdienste einschließlich ihrer
Registrierung und periodischen Ausführung. Konfigurationscontroller, Forms-Login,
Library-Scan, Health-Engine und Systemdienste folgen in Schritt 4; die Bedienoberfläche
folgt in Schritt 5. Der Start legt keine Spiele, Client-Zugangsdaten oder Indexer an.

## Komponenten

| Pfad unter `src` | Aufgabe |
|---|---|
| `Ludus.Application/Downloads` | Client-Verträge, normalisierte Zustände und Echtzeitereignisse |
| `Ludus.Infrastructure/DownloadClients` | qBittorrent, Transmission, SABnzbd und gemeinsamer HTTP-Transport |
| `Ludus.Infrastructure/Downloads` | Grab, CDH, Cooldown, Blocklist, Ersatzsuche und Client-Bereinigung |
| `Ludus.Infrastructure/Imports` | Pfadübersetzung, Archive, Dateisätze, Hashes, DAT-Abgleich, Naming, Recycling und Wiederaufnahme |
| `Ludus.Infrastructure/Workers` | Zeitplanung, Download-Refresh, RSS-Abgleich, Recycling-Aufräumen |
| `Ludus.Infrastructure/Notifications` | Persistente Webhook-Warteschlange und Testversand |
| `Ludus.Api/Hubs` | SignalR-Publisher und Zugangsschutz für `/hubs/activity` |

Die Migration `DownloadAutomation` ergänzt einen eindeutigen Submission-Token,
ausstehende Bereinigungs-/Ersatzsuchaufträge und `WebhookDeliveries`. Der vorhandene
Startpfad wendet sie automatisch an. Alte Einstellungen und Bibliothekseinträge
bleiben erhalten.

## Download-Clients und Isolation

Alle Adapter akzeptieren ausschließlich die Kategorie beziehungsweise das Label
`ludus`. Vor Lösch- und Tag-Operationen prüfen sie erneut die aktuelle Zuordnung.
Bereits vorhandene fremde Downloads werden nicht übernommen. Konfiguration und
Quell-URLs bleiben über die vorhandene Data-Protection-Abstraktion verschlüsselt.

| Client | API und Zuordnung | Importpfad und Abschluss |
|---|---|---|
| qBittorrent | Web API v2, Login-Cookie pro Aufruf, `category=ludus`, Submission-Tag | `content_path`; optionale Bereinigung erst bei gestopptem Torrent mit nachweislich erfülltem Ratio-/Zeitlimit |
| Transmission | Transmission 4.x, Legacy-RPC, HTTP-409-Session-Aushandlung, `labels=[ludus, token]` beim Hinzufügen | `downloadDir` und Dateiliste; `isFinished` steuert die optionale Entfernung |
| SABnzbd | JSON-API mit POST-Formular, `cat=ludus`, paginierte Queue und History | `history.storage`; Abschluss erst nach erfolgreicher Nachverarbeitung |

Transmission bietet in dieser RPC-Schnittstelle keinen serverseitigen Label-Filter.
Ludus liest deshalb zuerst ausschließlich Hashes und Labels und fordert detaillierte
Datei-/Fortschrittsdaten nur für passende Hashes an. Transmission-Versionen ohne
Labels beim Hinzufügen werden abgewiesen. Grundlage ist die
[offizielle RPC-Spezifikation](https://github.com/transmission/transmission/blob/4.0.6/docs/rpc-spec.md).

qBittorrent und Transmission erhalten optional `ludus-imported`. SABnzbd hat keinen
entsprechenden frei veränderbaren History-Tag; dort bleibt der Importstatus in Ludus
maßgeblich. Die Adapter orientieren sich an der
[qBittorrent Web API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29)
und der [SABnzbd API](https://sabnzbd.org/wiki/configuration/4.5/api).

Ein Grab wird vor dem HTTP-Aufruf mit einem eindeutigen Token gespeichert. Der Token
steht im Torrent-Tag/-Label beziehungsweise im SABnzbd-Auftragsnamen. Nach einem
Timeout oder Prozessabbruch sucht der nächste Refresh diesen Token. Unbestätigte
Aufträge werden nicht blind erneut gesendet; eine Health-Warnung fordert die
Klärung des Client-Zustands an. Das vermeidet doppelte Downloads bei verlorenen
HTTP-Antworten.

HTTP-Aufrufe haben ein 30-Sekunden-Limit und eine Antwortgrenze von 16 MiB.
Automatische Redirects und geteilte Cookie-Container sind ausgeschaltet.
Clientantworten, Authorization-Header, Webhook-URLs und Passwörter werden nicht
geloggt. Fehlercodes ersetzen möglicherweise sensitive Server-Fehlertexte.

## CDH und Fehlerbehandlung

Der Coordinator verarbeitet nur in Ludus registrierte Aufträge, die zusätzlich
zur isolierten Client-Kategorie gehören. Es gibt keinen Watch-Folder. Ist
Completed Download Handling deaktiviert, bleibt die Queue sichtbar und fertige
Aufträge können weiterhin manuell importiert werden; nur der automatische Import
ist ausgesetzt.

1. Queue-Daten aktualisieren: Status, Größe, Fortschritt, Geschwindigkeit und ETA.
2. Fertige Downloads anhand des vom Client gelieferten Pfads importieren.
3. Erst nach erfolgreichem Datenbank-Commit Tag setzen und gegebenenfalls bereinigen.
4. Torrents bis zum erfüllten Seed-Ziel erhalten; erfolgreiche Bereinigung löscht
   die Quelldaten nicht.

Tracker-/Netzwerkfehler erzeugen einen sieben Tage gültigen Cooldown. Beschädigte
Archive oder ungültige Importinhalte erzeugen eine permanente Sperre. Eine
permanente Sperre wird nicht zu einem Cooldown herabgestuft. Bei aktivem
`RemoveFailed` entfernt Ludus den fehlgeschlagenen Auftrag; Quelldaten werden nur
bei permanenter Sperre zur Löschung an den Client übergeben. Die dauerhaft
gespeicherte Grab-Herkunft unterscheidet automatische von interaktiven Grabs.
`Redownload Failed` und `Redownload Failed from Interactive Search` steuern die
Ersatzsuche daher unabhängig. Jede Ersatzsuche behält Plattform und Qualitätsprofil
bei und berücksichtigt Blocklist, Cooldown, Monitoring, Mindestscore und Cutoff.
Ein gespeicherter Verarbeitungszeitpunkt verhindert doppelte Fehlerverarbeitung.

Lokale Probleme wie fehlender Mount, Schreibrechte, voller Datenträger oder eine
Zielkollision lösen eine Warnung und einen späteren Importversuch aus. Sie sperren
das Release nicht. qBittorrent-Tracker werden für `stalledDL` geprüft; ein bloßes
Warten auf Peers führt nicht zur Sperre. SABnzbd liefert keine zuverlässige
Geschwindigkeit pro Job; dafür wird die Änderung der heruntergeladenen Bytes
zwischen zwei Refreshes verwendet.

## Pfade und Import

Remote Path Mappings vergleichen den Host und das vollständige Pfadpräfix mit
Verzeichnisgrenze. Bei mehreren Treffern gewinnt das längste Präfix. Ohne Mapping
muss der Content-Pfad innerhalb des konfigurierten Download-Verzeichnisses liegen.
Der Download-Root selbst ist kein zulässiger Content-Pfad. Für Transmission-Sets
ohne gemeinsamen Unterordner ist daher eine getrennte Ablage erforderlich.

Der Importer lehnt Traversal, Symlinks/Reparse-Points, fremde Zieldateien und
überschneidende Download-/Bibliothekspfade ab. Root Folders müssen bereits
existieren. Die Plattform legt fest, ob ZIP/7z entpackt werden. Dafür wird
SharpCompress verwendet; ein externes `7z`-Programm ist nicht nötig. Anzahl und
entpackte Gesamtgröße werden begrenzt. Verschlüsselte Archive, doppelte Ziele,
abgeschnittene Daten und Prüfsummenfehler werden abgewiesen. ZIP-CRC32 wird immer
verglichen, 7z-CRC32 zusätzlich, sofern die Bibliothek einen von null verschiedenen
Wert liefert. Das zugrunde liegende
[7z-Entry-Modell](https://github.com/adamhathcock/sharpcompress/blob/master/src/SharpCompress/Common/SevenZip/SevenZipEntry.cs)
repräsentiert eine fehlende CRC mit null.

Alle importierten Dateien erhalten CRC32, MD5 und SHA1 sowie den DAT-Status aus
Schritt 2. Ein fehlender oder abweichender DAT-Treffer verhindert den Import nicht;
er bleibt als Unverified beziehungsweise Modified erkennbar. Tatsächliche
ROM-/Deskriptornamen werden erneut gegen das Qualitätsprofil geprüft.

Einzeldateien nutzen das Naming-Schema oder einen eindeutigen kanonischen DAT-Namen.
Bei CUE/GDI-Sets bleiben referenzierte Namen und relative Verzeichnisse erhalten.
Fehlende Tracks verhindern den Import des gesamten Sets. Mehrere Discs erhalten
eine UTF-8-M3U mit relativen Verweisen. PS3-, Wii-U- und DOS-Ordnerspiele behalten
ihre Verzeichnisstruktur einschließlich Support-Dateien.

Switch-Title-IDs ordnen Updates und DLCs dem bereits registrierten Spiel zu;
widersprüchliche Basis-IDs werden abgewiesen. Zusatzinhalte liegen unter `Updates`
beziehungsweise `DLC`. Ein DLC allein macht ein fehlendes Basisspiel nicht verfügbar.

## Dateitransaktionen und Recycling

Neue Dateien werden zunächst im zugewiesenen Root unter `.ludus-staging/<id>`
vorbereitet und geprüft. Native Hardlinks werden bevorzugt. Da .NET 10 keinen
`File.CreateHardLink` bereitstellt, verwendet `AtomicFiles` die Betriebssystemaufrufe
`CreateHardLinkW` beziehungsweise `link`. Bei nicht unterstütztem Dateisystem oder
Mount-Grenzen erfolgt eine Kopie über eine temporäre Datei mit anschließendem Rename.
Die Download-Quelle bleibt zum Seeden erhalten.

Die Platzprüfung läuft auf dem tatsächlichen Ziellaufwerk und berücksichtigt
Dateigröße, konfigurierten Restfreiraum und Hardlinks. Bei Platzmangel bleibt ein
verständlicher Importstatus erhalten. Ludus verwaltet nur als eigene Spielordner
erfasste Verzeichnisse: leere Ordner für überwachte Spiele können angelegt und nur
dann entfernt werden, wenn sie tatsächlich leer sind. Root Folders, fremde
Verzeichnisse und Ziele außerhalb der Bibliothek werden nie entfernt.

Unter Linux können neue Verzeichnisse und kopierte Dateien einen konfigurierten
Modus sowie optional eine Gruppe erhalten. Nicht gesetzte Werte lassen UMASK und
Containerkonfiguration wirken. Bei Hardlinks werden Dateirechte nicht nachträglich
geändert, weil Quelle und Ziel denselben Inode teilen. Unter Windows kennzeichnet
die Oberfläche diese Optionen als nicht wirksam.

Vor Änderungen an bestehenden Bibliotheksdateien wird ein Journal unter
`/config/Imports/<download-id>.json` geschrieben. Alte Dateien werden zunächst in
den Recycling Bin kopiert und per SHA1 geprüft, dann aus ihrem bisherigen Ziel
entfernt. Neue Dateien werden einzeln atomar aus dem Staging veröffentlicht;
anschließend werden Dateizeilen, History, Importstatus und Webhook-Aufträge in
einer Datenbanktransaktion gespeichert.

Eine Veröffentlichung mehrerer Dateien ist keine einzelne atomare Dateisystemoperation.
Das Journal ermöglicht die Wiederaufnahme nach einem Prozessabbruch, auch wenn
der Download-Client inzwischen nicht erreichbar ist. Bereits veröffentlichte
Dateien werden vor der Fortsetzung geprüft. Externe Änderungen blockieren die
Wiederaufnahme statt Dateien ungeprüft zu überschreiben. Nicht abgeschlossene
Journale und ihre Recycling-Kopien dürfen nicht manuell entfernt werden.

Die Recycling-Bereinigung entfernt nur Ordner abgeschlossener Imports, deren
Importzeit außerhalb der konfigurierten Aufbewahrung liegt; Standard sind 30 Tage.
Für Hardlinks über Containergrenzen gilt weiterhin der gemeinsame Mount aus
`docker-compose.hardlinks.yml` und dem [Servarr Docker Guide](https://wiki.servarr.com/docker-guide).

## Worker, Webhooks und SignalR

`AutomationWorker` führt fällige Aufgaben anhand der Datenbankplanung aus. Der
Standard ist Download-Refresh alle 15 Sekunden, RSS-Sync alle 15 Minuten und
Recycling-Bereinigung täglich. Aufgaben laufen seriell, ohne überlappende Imports
oder Grabs. Ein langsamer Client kann nachfolgende Aufgaben verzögern. Beim Start
werden verwaiste laufende Task-Einträge als unterbrochen markiert.

Webhooks werden zusammen mit dem fachlichen Ereignis in `WebhookDeliveries`
gespeichert. Die Zustellung erfolgt mit exponentiellen Wiederholungen, höchstens
zehn Versuchen und maximal einer Stunde Abstand. Nach zehn Fehlern bleibt der
Auftrag zur Diagnose gespeichert. Die Zustellung erfolgt mindestens einmal;
ein Abbruch nach erfolgreichem HTTP-Aufruf kann eine doppelte Benachrichtigung
verursachen. Discord-Mentions sind deaktiviert. Mehrere benannte Verbindungen
können je Ereignis aktiviert und direkt getestet werden. Discord, Gotify, Telegram
und generische HTTP-Webhooks erhalten jeweils ihr natives Nachrichtenformat;
Tokens und geheime URLs werden mit dem vorhandenen Secret-Schutz gespeichert und
in Diagnoseausgaben bereinigt. Ein Zustellfehler verändert den Status eines bereits
erfolgreichen Imports nicht.

Der Hub `/hubs/activity` sendet `queueUpdated`, `importStatus`, `rssUpdated`,
`taskUpdated` und `healthWarning`. Handshake und Transport verlangen bereits den
`X-Api-Key`-Header; Query-String-Keys werden nicht unterstützt. Native SignalR-Clients
können diesen Header verwenden. Browser-WebSockets erhalten die Cookie-Session in
Schritt 4; bis dahin erfordert ein Browserclient einen Transport mit Header-Unterstützung.
Der anonyme Status-Endpunkt bleibt unverändert minimal.

## Verifikation und Grenzen

Die Tests verwenden echte temporäre SQLite-Datenbanken und Dateisysteme sowie
kontrollierte HTTP-Antworten für externe Dienste. Sie prüfen unter anderem
Kategorie-Isolation, Session-Aushandlung, Import/Upgrade, Seeding-Schutz,
Cooldown/Blocklist/Ersatzsuche, ZIP/7z, Archiv-Traversal und CRC-Fehler, CUE/GDI/M3U,
Ordnerspiele, DLC-Zuordnung, Hardlinks/Kopien, Wiederaufnahme, Webhook-Retry und
den geschützten SignalR-Handshake.

```powershell
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-restore
npm --prefix frontend run build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Ludus.Infrastructure --startup-project src/Ludus.Api --configuration Release --no-build
```

Live-Tests gegen echte Download-Clients, Tracker, Discord und einen Linux-Dockerhost
sind in dieser Umgebung nicht erfolgt. Die Client-Integration ist durch HTTP-Vertragstests
geprüft; der native Hardlink-Test lief auf Windows. Das vollständige Frontend und
die Konfigurations-REST-API gehören zu den folgenden Phasen.
