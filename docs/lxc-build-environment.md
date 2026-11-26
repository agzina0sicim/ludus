# LXC: .NET-Initialisierung als Build-Benutzer

Bei einer Installation unter Debian 13 brach `dotnet restore` mit `System.IO.IOException` für `NuGet-Migrations` ab. Der Frontend-Build war bereits erfolgreich; die systemd-Unit war zu diesem Zeitpunkt noch nicht angelegt. `/tmp` und `/dev/shm` hatten Modus `1777`. Ein anschließender Test als `ludus-build` mit eigenem Home-, XDG- und Temp-Verzeichnis lieferte erfolgreich SDK-Version `10.0.404`.

Der Installer übergibt diese Pfade jetzt explizit an alle Build-Befehle und prüft `dotnet --version` vor dem Frontend-Build. Die erfolgreich geprüfte Umgebung grenzt den Fehler ein; welcher bisherige Pfad den Mutex-Zugriff verhindert hat, ist nicht abschließend nachgewiesen. Deshalb werden keine globalen `/tmp`-Verzeichnisse gelöscht oder umberechtigt. Insbesondere verwendet .NET 10 unter Linux für benannte Mutexe weiterhin `/tmp`, unabhängig vom allgemeinen `TMPDIR`.

## Bestehenden Checkout im LXC korrigieren

Der folgende Patch ist für den Installer aus Release 0.1.0 gedacht. Er ergänzt dieselben Benutzerpfade direkt im bestehenden Build-Block und sichert die bisherige Datei. Auf dem LXC als root ausführen, wenn der aktualisierte Installer noch nicht aus dem Repository bezogen wurde:

```bash
cd /root/Ludus
if ! grep -q '# Ludus build-home fix' install-lxc.sh; then
    sed -i.before-build-env-fix '/^[[:space:]]*cd "\$1\/source"$/a\
            # Ludus build-home fix\
            export HOME="$1/home" TMPDIR="$1/tmp" XDG_RUNTIME_DIR="$1/runtime"\
            export XDG_CONFIG_HOME="$1/home/.config" XDG_DATA_HOME="$1/home/.local/share" XDG_CACHE_HOME="$1/cache"\
            mkdir -p "$HOME" "$TMPDIR" "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME"\
            chmod 0700 "$HOME" "$TMPDIR" "$XDG_RUNTIME_DIR"\
            dotnet --version || exit $?
' install-lxc.sh
fi
bash -n install-lxc.sh
```

Danach dieselbe Installation erneut starten:

```bash
bash install-lxc.sh --media-dir /data --uid 1000 --gid 1000 --bind 0.0.0.0
```

Die bereits angelegten Benutzer und Verzeichnisse werden weiterverwendet. Erst nach erfolgreichem Build und Aktivierung sind `systemctl status ludus` und der Zugriff auf Port 8080 sinnvoll. Den lokalen Patch bei einem späteren Git-Update mit der korrigierten Repository-Version abgleichen.

Die Ergänzung wurde mit Bash/ShellCheck und Regressionstests für die übergebenen Benutzerpfade geprüft. Der erfolgreiche SDK-Start wurde im betroffenen LXC bestätigt; der vollständige Installationslauf steht noch aus.
