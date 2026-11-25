# Schritt 2 – Releases, DAT-Verifikation, Scoring und Indexer

Die Komponenten sind über `AddInfrastructure` registriert. Sie benötigen keine
neuen NuGet-Pakete und ändern das Datenbankschema nicht. Controller, periodische
RSS-Worker und Download-Auslösung folgen entsprechend der vorgegebenen Reihenfolge
in den Schritten 3 und 4.

## Einstiegspunkte

| Komponente | Datei / Vertrag | Aufgabe |
|---|---|---|
| Release-Parser | `Ludus.Application/Releases/ReleaseParser.cs` | Titel, Regionen, Sprachen, Revision, Disc, Status, Seriennummer und Switch-Identität |
| Custom Formats | `Ludus.Application/Profiles/CustomFormatEvaluator.cs` | All/Any, Pflichtbedingungen, Negation, Regex und numerischer Versionsvergleich |
| Score und Entscheidung | `Ludus.Application/Profiles/ReleaseScoringEngine.cs` | Score-Beiträge, Cutoff, Bestand, Sperren und zweisprachige Ablehnungsgründe |
| DAT-Leser | `Ludus.Infrastructure/Verification/LogiqxDatReader.cs` | Streaming-Leser für Logiqx-ROM-Daten |
| DAT-Import | `IDatImporter` | Plattformgebundener, transaktionaler und anhand SHA-256 idempotenter Import |
| Datei-Hashes | `IFileHasher` | CRC32/IEEE, MD5 und SHA1 in einem sequenziellen Durchlauf |
| DAT-Abgleich | `IDatVerifier` | Plattform- und statusbewusste Prüfung aller vorhandenen Prüfsummen |
| Indexer-Transport | `IIndexerClient` / `TorznabClient` | Capabilities, Suche, RSS, begrenzte Antworten und sichere Fehler |
| Interaktive Suche | `IInteractiveSearch` | Suche über aktive Indexer, Pagination, Scoring und vollständige Trefferliste |

Die Application-Schicht bleibt frei von EF- und HTTP-Abhängigkeiten. DAT-Import,
Verifikation und interaktive Suche verwenden eigene DbContext-Instanzen über
eine scoped Factory. Ein Import leert dadurch nicht den Change Tracker eines
anderen Anwendungsfalls.

## Parsing und Identität

Der Parser entfernt bekannte Metadaten-Tags und PlayStation-Serials aus dem
Suchnamen, lässt aber unbekannte Titelzusätze wie `(Special Edition)` stehen.
Seriennummern wie `SLUS_123.45`, `CUSA12345` und `PPSA-12345` werden normalisiert.
Regionen und Sprachen können mehrere Werte enthalten. `[!]` wird ausdrücklich
nur als Namens-Tag gespeichert; eine DAT-Verifikation wird daraus nicht abgeleitet.
Auch nummerierte Beta-/Demo-Tags, Übersetzungen, Hacks und Disc-Angaben werden erkannt.
Nachgestellte Artikel werden für den Titelvergleich normalisiert, Fortsetzungsnummern
bleiben erhalten. Identitäten haben beim Spielvergleich Vorrang vor Titeln.

Switch-IDs werden als 64-Bit-Werte im Application-Bereich ausgewertet. Innerhalb
des durch `0x1FFF` begrenzten Blocks sind `0x0000` die Basis, `0x0800` das Update
und Werte oberhalb `0x1000` DLCs. `id & ~0x1FFF` liefert die Basis-ID.
Dadurch wird eine DLC-ID mit den letzten drei Ziffern `800` nicht versehentlich
als Spiel-Update behandelt. Programm-Unter-IDs und die reine DLC-Basisadresse
werden nicht als eigenständiger DLC interpretiert. Widersprüchliche Tags und
mehrere unterschiedliche Title-IDs erzeugen Ablehnungsgründe.

Dies ist die Identifikation aus Release-Namen. Sie ersetzt keine Untersuchung
der tatsächlichen Container-Inhalte. Die Zuordnung verwendet keine Entschlüsselung
und lädt keine Schlüssel oder ROMs herunter.

## DAT-Import und Abgleich

Der Import akzeptiert `<game>` und `<machine>` sowie ROM-Daten mit `crc`, `md5`,
`sha1`, Größe, Name, Status und Merge-Verweis. Header-Name, Beschreibung, Version
und Autor werden übernommen. SHA-256 des Uploads verhindert doppelte Importe
derselben Datei auf derselben Plattform. Ein neuer Katalog ersetzt ältere Kataloge
nicht automatisch; `DatFile.Enabled` steuert ihre Teilnahme am Abgleich.

Der Upload wird in einer automatisch entfernten temporären Datei unter Config
gepuffert. Grenzen: 128 MiB, 250.000 Spiele, eine Million ROM-Einträge. Der
XML-Leser arbeitet spielweise; der Import schreibt alle 100 Spiele innerhalb
einer gemeinsamen Transaktion. Ein Fehler am Dateiende rollt auch vorherige
Batches zurück. Externe DTD-Deklarationen werden ignoriert, kein XML-Resolver
ist aktiv. Benutzerdefinierte Entity-Referenzen werden nicht expandiert.

Eine rohe ROM-Datei wird nur dann `VerifiedGoodDump`, wenn alle im passenden
DAT-Eintrag vorhandenen Hashes und eine vorhandene Größe übereinstimmen, mindestens
MD5 oder SHA1 vorliegt und der Dump-Status gut ist. Alle passenden kanonischen
Namen werden zurückgegeben; bei mehreren Treffern wird nicht willkürlich einer
ausgewählt. CRC32 allein bleibt unbestätigt. Ein bekannter Bad Dump oder ein
widersprüchlicher Hash wird `Modified`, ein unbekannter Dump `Unverified`.
Ein erwarteter kanonischer Name kann zusätzlich helfen, modifizierte ROMs zu erkennen.
Deaktivierte DATs und andere Plattformen sind vom Abgleich ausgeschlossen.

Die Hashes beziehen sich auf die gelesenen Datei-Bytes. Ein ZIP-Archiv oder ein
CHD-Container wird damit nicht automatisch gegen den Hash seiner entpackten
Nutzdaten verifiziert. Logiqx-`<disk>`-Einträge sind deshalb ausdrücklich abgelehnt:
deren logische Disk-Hashes dürfen nicht mit physischen Datei-Hashes verwechselt
werden. Der unterstützte Importumfang sind die verlangten ROM-Einträge aus
No-Intro-/Redump-Katalogen, einschließlich Mehrdatei-ROM-Sets.

## Score, Cutoff und Ablehnungen

Jeder Treffer enthält Score, einzelne Custom-Format-Beiträge und strukturierte
Gründe mit stabilem Code sowie deutschem und englischem Text. Die Engine prüft
Regionen, Beta/Demo/Proto, Plattform-Endung, Mindestscore, Update-/DLC-Freigaben,
Identität, Cooldowns und permanente Sperren. Automatische Auswertung berücksichtigt
Monitoring; eine manuelle Suche bleibt auch bei unüberwachten Spielen möglich.

Bestand unterhalb des Cutoffs darf nur bei aktivierten Upgrades durch einen
höheren Score ersetzt werden. Fehlende Dateien blockieren keinen Ersatz.
Die Prüfung trennt Basis, Updates, einzelne DLC-IDs und Disc-Nummern. Ein
Basisspiel am Cutoff blockiert weder neuen DLC noch eine andere fehlende Disc.
Für die Bestandsbewertung wird der gespeicherte Datei-Score verwendet.

Custom-Format-Regeln unterstützen All/Any, Required und Negate. Numerische
Revisionen werden komponentenweise verglichen: 1.10 ist größer als 1.2.
Regex-Regeln haben eine Laufzeitgrenze von 50 ms. Ungültige oder zu langsame
Regeln führen zu einer sichtbaren Ablehnung statt zu einem stillschweigend
positiven Ergebnis. Überlaufende Scores werden ebenfalls abgelehnt.

## Torznab/Newznab

Der Client unterstützt Prowlarr-Endpunkte und Jackett-Pfade, Query-Encoding,
`caps`, Suchtext, Kategorien, Offset und Limit. RSS verwendet standardkonform
`t=search` ohne `q`. Vor RSS und vor der interaktiven Suche werden Capabilities
abgefragt; deren Limits werden berücksichtigt. Beide Attribut-Namespaces,
mehrfach vorkommende Kategorien, Enclosure-Links, Magnet-Links, Größe, Seeder,
Peers und Veröffentlichungszeit werden gelesen.

Die interaktive Suche fragt alle dafür aktivierten Indexer ab. Fehlgeschlagene
Indexer liefern eigene Fehler; erfolgreiche Teilresultate bleiben sichtbar.
Pro Indexer werden höchstens 20 Seiten mit jeweils bis zu 100 Treffern verarbeitet.
Erreichte Grenzen, wiederholte Seiten und unvollständige Pagination werden gemeldet.
Ablehnungen werden nicht ausgefiltert. Fehlende Download-URLs, widersprüchliche
Kategorien, fehlerhafte Feed-Einträge und zu wenige Seeder bleiben sichtbare Gründe.

HTTP-Anfragen haben 30 Sekunden Gesamtzeit einschließlich Body, maximal 16 MiB
dekomprimierte Antwort und keine automatischen Redirects. Feed-DTDs werden
abgelehnt. Die HttpClient-Logger sind für diesen Client entfernt, damit
Indexer-Keys in Query-Strings nicht geloggt werden. HTTP-/XML-Fehler werden ohne
Original-URL oder Serverbeschreibung weitergegeben; 429 liefert Retry-After.

Download-URLs werden sofort mit Data Protection geschützt und sind von der
JSON-Serialisierung ausgeschlossen. Release-IDs und Fingerprints sind Hashes;
GUIDs, die Zugriffstoken enthalten können, erscheinen nicht unverändert im
Ergebnis. Es werden in diesem Schritt keine Download-Links aufgerufen.

## Verifikation

Die xUnit-Suite umfasst reale SQLite-Importe und HTTP-Tests mit einem kontrollierten
`HttpMessageHandler`. Sie prüft bekannte Hash-Testvektoren, DAT-Rollback nach
bereits geschriebenen Batches, XXE-Abwehr, Duplicate-Import, deaktivierte Kataloge,
Parser-Grenzfälle, Cutoffs, Sperren, beide Feed-Namespaces, Query-Encoding,
Pagination, Teilfehler, Redaction, Redirects und Antwortgrößen.

Ein beim erweiterten Testlauf sichtbares Shutdown-Rennen wurde behoben: Der Host
besitzt nun seinen Logger; die Prozesssperre wird bei `ApplicationStopped`
freigegeben. Damit hängen Datei-Handles nicht mehr an einem globalen Logger.

```powershell
dotnet build src/Ludus.sln -c Release --no-restore
dotnet test src/Ludus.sln -c Release --no-restore
npm --prefix frontend run build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Ludus.Infrastructure --startup-project src/Ludus.Api --configuration Release --no-build
```

Live-Tests gegen private Indexer sind ohne konfigurierte Endpunkte und Credentials
nicht erfolgt. Die REST-Routen und Weboberfläche für diese Komponenten sind gemäß
Phasenplan noch nicht Bestandteil dieses Schritts.

## Quellen

- [Torznab-Spezifikation](https://torznab.github.io/spec-1.3-draft/torznab/Specification-v1.3.html): Capabilities, Search/RSS, Namespaces, Kategorien und Pagination.
- [Logiqx-Format und DTD bei SabreTools](https://github.com/SabreTools/SabreTools/wiki/Logiqx-XML): ROM-/Disk-Unterscheidung, Header, Prüfsummen und Dump-Status.
- [Atmosphère: Application-ID-Bereich](https://github.com/Atmosphere-NX/Atmosphere/blob/master/libraries/libstratosphere/include/stratosphere/ncm/ncm_content_meta_id.hpp).
- [cargo-nx: Add-on-Content-Basis](https://github.com/aarch64-switch-rs/cargo-nx): DLC-Basisadresse relativ zur Application-ID.
