#!/usr/bin/env bash
set -Eeuo pipefail

log() { printf '[ludus] %s\n' "$*"; }
die() { printf '[ludus] ERROR: %s\n' "$*" >&2; exit 1; }

usage() {
    cat <<'HELP'
Usage: bash install-lxc.sh [options]
Run as root INSIDE a Debian 12/13 LXC, from a trusted Ludus checkout.
The same command installs a new instance or updates an existing installation.

  --source PATH          Ludus checkout (default: script directory)
  --config-dir PATH      Persistent configuration (default: /config)
  --downloads-dir PATH   Download directory (default: /downloads)
  --roms-dir PATH        Library parent directory (default: /roms)
  --media-dir PATH       Use PATH/downloads and PATH/roms with one writable mount
  --uid NUMBER           UID of the ludus service account (default: 1000)
  --gid NUMBER           Primary GID; an existing group can be reused (default: 1000)
  --bind ADDRESS         127.0.0.1 or 0.0.0.0 (default: 127.0.0.1)
  --port NUMBER          Unprivileged HTTP port (default: 8080)
  --node-version VERSION Explicit Node 24 release, e.g. 24.22.3 (default: latest 24.x)
  --help                 Show help without changing the system

Updates reuse /etc/ludus/install-state. Storage paths and UID/GID cannot be
changed during an update. Use systemd drop-ins for additional writable roots.
Only the config tree receives recursive ownership changes. Existing download
and ROM directories are checked, never recursively chowned or chmodded.
HELP
}

validate_id() {
    if [[ ! $2 =~ ^[1-9][0-9]{0,9}$ ]] || ((10#$2 > 2147483647)); then die "$1 must be between 1 and 2147483647."; fi
}

validate_storage_path() {
    local value=$1 part current='' canonical
    local -a parts
    [[ $value =~ ^/[a-zA-Z0-9_./+-]+$ ]] || die "Storage paths must be absolute and contain no spaces or shell/systemd metacharacters: $value"
    canonical=$(readlink -m -- "$value")
    [[ $value == "$canonical" ]] || die "Use a canonical path without symlinks, '..' or trailing '/': $value"
    case "$value" in
        /|/etc|/etc/*|/usr|/usr/*|/bin|/bin/*|/sbin|/sbin/*|/lib|/lib/*|/lib64|/lib64/*|/proc|/proc/*|/sys|/sys/*|/dev|/dev/*|/run|/run/*|/tmp|/tmp/*|/var/tmp|/var/tmp/*|/root|/root/*|/home|/home/*|/opt|/opt/ludus|/opt/ludus/*|/var|/var/lib)
            die "Unsafe or service-inaccessible storage path: $value" ;;
    esac
    IFS=/ read -r -a parts <<< "$value"
    for part in "${parts[@]}"; do
        [[ -n $part ]] || continue
        current+="/$part"
        [[ ! -L $current ]] || die "Symlink in storage path: $current"
        [[ ! -e $current || -d $current ]] || die "Storage component is not a directory: $current"
    done
}

paths_overlap() { [[ $1 == "$2" || $1 == "$2/"* || $2 == "$1/"* ]]; }

validate_options() {
    validate_id UID "$service_uid"
    validate_id GID "$service_gid"
    if [[ ! $port =~ ^[1-9][0-9]{3,4}$ ]] || ((10#$port < 1024 || 10#$port > 65535)); then die 'Port must be between 1024 and 65535.'; fi
    [[ $bind_address == 127.0.0.1 || $bind_address == 0.0.0.0 ]] || die 'Bind address must be 127.0.0.1 or 0.0.0.0.'
    [[ $node_version == latest || $node_version =~ ^24\.[0-9]+\.[0-9]+$ ]] || die 'Node version must be latest or an explicit stable 24.x.y release.'
    validate_storage_path "$config_dir"
    validate_storage_path "$downloads_dir"
    validate_storage_path "$roms_dir"
    if paths_overlap "$config_dir" "$downloads_dir" || paths_overlap "$config_dir" "$roms_dir" || paths_overlap "$downloads_dir" "$roms_dir"; then
        die 'Configuration, downloads and library directories must not overlap.'
    fi
    if [[ -n $media_dir ]]; then
        validate_storage_path "$media_dir"
        [[ $downloads_dir == "$media_dir/"* && $roms_dir == "$media_dir/"* ]] || die 'Downloads and ROMs must be below --media-dir.'
        if paths_overlap "$config_dir" "$media_dir"; then die 'Configuration must be outside --media-dir.'; fi
    fi
}

read_state() {
    local key value
    [[ -f $state_file && ! -L $state_file ]] || die 'Invalid installer state file.'
    [[ $(stat -c %u "$state_file") == 0 && $(stat -c %a "$state_file") == 600 ]] || die 'Installer state must be owned by root with mode 0600.'
    while IFS='=' read -r key value; do
        case "$key" in
            CONFIG_DIR) config_dir=$value ;;
            DOWNLOADS_DIR) downloads_dir=$value ;;
            ROMS_DIR) roms_dir=$value ;;
            MEDIA_DIR) media_dir=$value ;;
            SERVICE_UID) service_uid=$value ;;
            SERVICE_GID) service_gid=$value ;;
            BIND_ADDRESS) bind_address=$value ;;
            PORT) port=$value ;;
            '') ;;
            *) die "Unknown installer state field: $key" ;;
        esac
    done < "$state_file"
}

render_environment() {
    cat <<ENVIRONMENT
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://$bind_address:$port
DOTNET_EnableDiagnostics=0
DOTNET_CLI_TELEMETRY_OPTOUT=1
Ludus__ConfigDirectory=$config_dir
Ludus__DownloadDirectory=$downloads_dir
Ludus__RomDirectory=$roms_dir
ENVIRONMENT
}

render_service() {
    local writable_media="$downloads_dir $roms_dir"
    [[ -z $media_dir ]] || writable_media=$media_dir
    cat <<SERVICE
# Managed by install-lxc.sh. Put local changes in systemctl edit ludus.
[Unit]
Description=Ludus retro game library
Wants=network-online.target
After=network-online.target
RequiresMountsFor=$config_dir $downloads_dir $roms_dir
StartLimitIntervalSec=300
StartLimitBurst=5

[Service]
Type=simple
User=ludus
Group=$service_gid
WorkingDirectory=/opt/ludus/current
EnvironmentFile=/etc/ludus/ludus.env
ExecStart=/usr/bin/dotnet /opt/ludus/current/Ludus.Api.dll
Restart=always
RestartSec=5
TimeoutStopSec=120
KillSignal=SIGTERM
UMask=0002
NoNewPrivileges=true
CapabilityBoundingSet=
AmbientCapabilities=
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$config_dir $writable_media
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6

[Install]
WantedBy=multi-user.target
SERVICE
}

download() { curl --fail --show-error --silent --location --proto '=https' --proto-redir '=https' --tlsv1.2 --retry 3 --connect-timeout 20 --max-time 900 "$1" -o "$2"; }

install_dependencies() {
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-install-recommends ca-certificates curl xz-utils tar gzip coreutils findutils util-linux passwd iproute2
    download "https://packages.microsoft.com/config/debian/$debian_version/packages-microsoft-prod.deb" "$work_dir/microsoft.deb"
    dpkg -i "$work_dir/microsoft.deb"
    apt-get update
    apt-get install -y --no-install-recommends dotnet-sdk-10.0 aspnetcore-runtime-10.0
    /usr/bin/dotnet --list-sdks | grep -q '^10\.' || die '.NET 10 SDK is unavailable.'
}

install_node() {
    local base manifest filename digest node_release
    local -a matches
    if [[ $node_version == latest ]]; then base=https://nodejs.org/dist/latest-v24.x; else base="https://nodejs.org/dist/v$node_version"; fi
    manifest="$work_dir/SHASUMS256.txt"
    download "$base/SHASUMS256.txt" "$manifest"
    mapfile -t matches < <(awk -v arch="$node_arch" '$2 ~ "^node-v24\\.[0-9]+\\.[0-9]+-linux-" arch "\\.tar\\.xz$" { print $1 " " $2 }' "$manifest")
    [[ ${#matches[@]} == 1 ]] || die 'Node checksum manifest must contain exactly one matching archive.'
    read -r digest filename <<< "${matches[0]}"
    [[ $digest =~ ^[a-fA-F0-9]{64}$ ]] || die 'Invalid Node SHA256 checksum.'
    node_release=${filename%.tar.xz}
    node_dir="/opt/ludus/tools/$node_release"
    if [[ ! -d $node_dir ]]; then
        download "$base/$filename" "$work_dir/$filename"
        printf '%s  %s\n' "$digest" "$work_dir/$filename" | sha256sum --check --status || die 'Node archive checksum mismatch.'
        mkdir "$work_dir/node"
        tar --extract --xz --file "$work_dir/$filename" --directory "$work_dir/node" --no-same-owner
        [[ -x $work_dir/node/$node_release/bin/node ]] || die 'Node archive is incomplete.'
        mv -- "$work_dir/node/$node_release" "$node_dir"
        chmod -R a+rX,go-w "$node_dir"
    fi
    [[ $("$node_dir/bin/node" --version) == v24.* ]] || die 'Node 24 is required.'
}

prepare_accounts() {
    if getent passwd ludus >/dev/null; then
        [[ $(id -u ludus) == "$service_uid" && $(id -g ludus) == "$service_gid" ]] || die 'Existing ludus UID/GID differs; account IDs are never changed automatically.'
    else
        ! getent passwd "$service_uid" >/dev/null || die 'Requested UID belongs to another user. Choose a free --uid.'
        if ! getent group "$service_gid" >/dev/null; then
            ! getent group ludus >/dev/null || die 'Group ludus exists with a different GID.'
            groupadd --gid "$service_gid" ludus
        fi
        useradd --uid "$service_uid" --gid "$service_gid" --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin ludus
    fi
    if ! getent passwd ludus-build >/dev/null; then
        useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin ludus-build
    fi
    [[ $(id -u ludus-build) != 0 && $(id -u ludus-build) != "$service_uid" ]] || die 'Build user must be separate from root and the service user.'
}

prepare_storage() {
    local directory
    [[ ! -L $config_dir/keys ]] || die 'The key directory must not be a symlink.'
    mkdir -p "$config_dir/keys"
    find "$config_dir" -xdev -exec chown -h "$service_uid:$service_gid" {} +
    chmod 0750 "$config_dir"
    chmod 0700 "$config_dir/keys"
    for directory in "$downloads_dir" "$roms_dir"; do
        if [[ ! -d $directory ]]; then install -d -o "$service_uid" -g "$service_gid" -m 2775 "$directory"; fi
        if ! runuser -u ludus -- test -r "$directory" || ! runuser -u ludus -- test -w "$directory" || ! runuser -u ludus -- test -x "$directory"; then
            die "ludus cannot access $directory. Correct mount ownership/ACLs outside this installer."
        fi
    done
}

run_build_command() {
    local build_dir=$1
    shift
    runuser -u ludus-build -- env PATH="$node_dir/bin:/usr/bin:/bin" \
        HOME="$build_dir/home" DOTNET_CLI_HOME="$build_dir/dotnet" TMPDIR="$build_dir/tmp" \
        XDG_RUNTIME_DIR="$build_dir/runtime" XDG_CONFIG_HOME="$build_dir/home/.config" \
        XDG_DATA_HOME="$build_dir/home/.local/share" XDG_CACHE_HOME="$build_dir/cache" \
        NUGET_PACKAGES="$build_dir/cache/nuget" npm_config_cache="$build_dir/cache/npm" \
        DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$@"
}

build_release() {
    local build_dir="$work_dir/build" invalid
    mkdir -p "$build_dir/source" "$build_dir/cache" "$build_dir/dotnet" \
        "$build_dir/home/.config" "$build_dir/home/.local/share" "$build_dir/tmp" "$build_dir/runtime"
    tar -C "$source_dir" --exclude=node_modules --exclude=bin --exclude=obj --exclude=dist --exclude=wwwroot --exclude='.env*' \
        -cf "$work_dir/source.tar" global.json Directory.Build.props Directory.Packages.props NuGet.Config src frontend scripts/ludus-launcher.sh
    tar --extract --file "$work_dir/source.tar" --directory "$build_dir/source" --no-same-owner
    chown -R -h ludus-build: "$build_dir"
    chmod 0700 "$build_dir/home" "$build_dir/tmp" "$build_dir/runtime"
    chmod 0755 "$work_dir"
    log 'Checking .NET initialization as ludus-build before building the frontend.'
    if ! run_build_command "$build_dir" dotnet --version; then
        runuser -u ludus-build -- id >&2 || true
        stat -c '%a %U:%G %n' /tmp /dev/shm >&2 || true
        find /tmp -maxdepth 2 -name '.dotnet*' -printf '%m %u:%g %p\n' >&2 || true
        die '.NET initialization failed as ludus-build. Check the diagnostics above and container restrictions; no service was activated.'
    fi
    # Expansion belongs to the unprivileged child shell, not the installer.
    # shellcheck disable=SC2016
    run_build_command "$build_dir" bash -Eeuo pipefail -c '
            cd "$1/source"
            npm --prefix frontend ci --no-audit --no-fund
            npm --prefix frontend run build
            dotnet restore src/Ludus.Api/Ludus.Api.csproj --locked-mode
            mkdir -p src/Ludus.Api/wwwroot
            cp -a frontend/dist/. src/Ludus.Api/wwwroot/
            dotnet publish src/Ludus.Api/Ludus.Api.csproj -c Release --no-restore -o "$1/publish" /p:UseAppHost=false
            install -m 0755 scripts/ludus-launcher.sh "$1/publish/Ludus.Api"
        ' bash "$build_dir"
    [[ -s $build_dir/publish/Ludus.Api.dll && -x $build_dir/publish/Ludus.Api && -s $build_dir/publish/wwwroot/index.html ]] || die 'Published release is incomplete.'
    invalid=$(find "$build_dir/publish" ! -type f ! -type d -print -quit)
    [[ -z $invalid ]] || die 'Publish output contains a symlink or special file.'
    release_dir="/opt/ludus/releases/$release_id"
    mkdir "$release_dir"
    cp -a "$build_dir/publish/." "$release_dir/"
    chown -R root:root "$release_dir"
    chmod -R a+rX,go-w "$release_dir"
}

save_state() {
    cat <<STATE
CONFIG_DIR=$config_dir
DOWNLOADS_DIR=$downloads_dir
ROMS_DIR=$roms_dir
MEDIA_DIR=$media_dir
SERVICE_UID=$service_uid
SERVICE_GID=$service_gid
BIND_ADDRESS=$bind_address
PORT=$port
STATE
}

activate_release() {
    local previous='' attempt
    if [[ -L /opt/ludus/current ]]; then previous=$(readlink -f /opt/ludus/current); fi
    if systemctl is-active --quiet ludus.service; then was_running=true; fi
    systemctl stop ludus.service 2>/dev/null || { [[ ! -e /etc/systemd/system/ludus.service ]] || die 'Could not stop existing service.'; }
    stopped=true
    [[ -z $(ss -H -ltn "sport = :$port") ]] || die "Port $port is already in use by another process."
    if [[ -f $config_dir/ludus.db ]]; then
        rollback_dir="/opt/ludus/rollback/$release_id"
        install -d -m 0700 "$rollback_dir"
        log "Creating offline pre-update configuration archive in $rollback_dir"
        tar --create --gzip --one-file-system --file "$rollback_dir/config.tar.gz" --exclude=./Backups --exclude=./logs -C "$config_dir" .
        chmod 0600 "$rollback_dir/config.tar.gz"
        [[ -z $previous ]] || printf '%s\n' "$previous" > "$rollback_dir/previous-release"
        [[ ! -f /etc/ludus/ludus.env ]] || cp -p /etc/ludus/ludus.env "$rollback_dir/"
        [[ ! -f /etc/systemd/system/ludus.service ]] || cp -p /etc/systemd/system/ludus.service "$rollback_dir/"
    fi
    # After this point, a failure leaves the service stopped. A migrated database
    # must not be opened automatically by an older binary.
    activating=true
    render_environment > "$work_dir/ludus.env"
    render_service > "$work_dir/ludus.service"
    systemd-analyze verify "$work_dir/ludus.service"
    install -m 0600 "$work_dir/ludus.env" /etc/ludus/ludus.env
    install -m 0644 "$work_dir/ludus.service" /etc/systemd/system/ludus.service
    save_state > "$work_dir/install-state"
    install -m 0600 "$work_dir/install-state" "$state_file"
    ln -s "$release_dir" "/opt/ludus/.current-$release_id"
    mv -Tf "/opt/ludus/.current-$release_id" /opt/ludus/current
    systemctl daemon-reload
    # A newly installed unit may not be loaded yet and has no failure state to reset.
    systemctl reset-failed ludus.service 2>/dev/null || true
    systemctl enable --now ludus.service
    for ((attempt=0; attempt<90; attempt++)); do
        if systemctl is-active --quiet ludus.service && curl --fail --silent --noproxy '*' --max-time 2 "http://127.0.0.1:$port/api/v1/system/status" | grep -qx '{"status":"ok"}'; then
            activating=false
            log "Ready on port $port (bind $bind_address)."
            log "Bootstrap key, if forms login is disabled: $config_dir/bootstrap-api-key"
            log 'Manage with systemctl status ludus; inspect logs with journalctl -u ludus.'
            return
        fi
        sleep 2
    done
    die 'Startup health check failed. Inspect journalctl -u ludus and the config logs; service will be stopped.'
}

cleanup() {
    local status=$?
    trap - EXIT
    if ((status != 0)); then
        if [[ $activating == true ]]; then
            systemctl stop ludus.service || true
            printf '[ludus] Activation failed. Retained release: %s; recovery archive: %s\n' "${release_dir:-none}" "${rollback_dir:-none}" >&2
        elif [[ $stopped == true && $was_running == true ]]; then
            systemctl start ludus.service || true
        fi
    fi
    if [[ ${work_dir:-} =~ ^/var/tmp/ludus-install\.[a-zA-Z0-9]+$ && -d $work_dir && ! -L $work_dir ]]; then
        rm -rf --one-file-system -- "$work_dir" || printf '[ludus] Could not clean build directory: %s\n' "$work_dir" >&2
    fi
    exit "$status"
}

main() {
    local source_dir config_dir=/config downloads_dir=/downloads roms_dir=/roms media_dir='' service_uid=1000 service_gid=1000
    local bind_address=127.0.0.1 port=8080 node_version=latest state_file=/etc/ludus/install-state old_storage='' existing=false
    local debian_version node_arch node_dir='' release_id managed_path
    # EXIT runs after main returns; these values must outlive the function scope.
    work_dir='' release_dir='' rollback_dir='' activating=false stopped=false was_running=false
    source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
    if [[ ${1:-} == --help || ${1:-} == -h ]]; then usage; return; fi
    [[ $EUID == 0 ]] || die 'Run this installer as root inside the Debian container.'
    [[ -d /run/systemd/system && -r /etc/os-release ]] || die 'A booted Debian container with systemd is required.'
    [[ ! -d /etc/pve ]] || die 'Do not run on the Proxmox host; run inside the target LXC.'
    # /etc/os-release exists only on the target Debian machine.
    # shellcheck source=/dev/null
    [[ $(. /etc/os-release; printf '%s' "$ID") == debian ]] || die 'Only Debian is supported.'
    # shellcheck source=/dev/null
    debian_version=$(. /etc/os-release; printf '%s' "$VERSION_ID")
    [[ $debian_version == 12 || $debian_version == 13 ]] || die 'Debian 12 or 13 is required.'
    case "$(dpkg --print-architecture)" in amd64) node_arch=x64 ;; arm64) node_arch=arm64 ;; *) die 'Only amd64 and arm64 are supported.' ;; esac
    for managed_path in /etc/ludus /opt/ludus /opt/ludus/releases /opt/ludus/tools /opt/ludus/rollback /etc/ludus/ludus.env /etc/ludus/install.lock /etc/systemd/system/ludus.service; do
        [[ ! -L $managed_path ]] || die "Installer-managed path must not be a symlink: $managed_path"
    done
    if [[ -e $state_file || -L $state_file ]]; then
        read_state
        existing=true
        old_storage="$config_dir|$downloads_dir|$roms_dir|$media_dir|$service_uid|$service_gid"
    fi
    while (($#)); do
        [[ $# -ge 2 ]] || die "Missing value for $1"
        case "$1" in
            --source) source_dir=$2 ;; --config-dir) config_dir=$2 ;; --downloads-dir) downloads_dir=$2 ;;
            --roms-dir) roms_dir=$2 ;; --uid) service_uid=$2 ;; --gid) service_gid=$2 ;;
            --media-dir) media_dir=$2; downloads_dir="$2/downloads"; roms_dir="$2/roms" ;;
            --bind) bind_address=$2 ;; --port) port=$2 ;; --node-version) node_version=$2 ;;
            *) die "Unknown option: $1" ;;
        esac
        shift 2
    done
    validate_options
    if [[ $existing == true && $old_storage != "$config_dir|$downloads_dir|$roms_dir|$media_dir|$service_uid|$service_gid" ]]; then die 'Storage paths and UID/GID cannot be changed during an update.'; fi
    source_dir=$(readlink -e -- "$source_dir")
    [[ -f $source_dir/src/Ludus.Api/Ludus.Api.csproj && -f $source_dir/frontend/package-lock.json ]] || die 'Source is not a complete Ludus checkout.'
    [[ ! -e /opt/ludus/current || -L /opt/ludus/current ]] || die '/opt/ludus/current must be an installer-managed symlink.'
    if [[ -L /opt/ludus/current ]]; then
        [[ $(readlink -f /opt/ludus/current) == /opt/ludus/releases/* ]] || die 'Current release points outside the managed releases directory.'
        [[ -f /opt/ludus/current/Ludus.Api.dll ]] || die 'Current release is incomplete.'
    fi
    [[ $existing == true || ! -e /etc/systemd/system/ludus.service ]] || die 'Unmanaged ludus.service already exists.'
    install -d -m 0755 /opt/ludus /opt/ludus/releases /opt/ludus/tools
    install -d -m 0700 /etc/ludus /opt/ludus/rollback
    exec 9>/etc/ludus/install.lock
    flock -n 9 || die 'Another installer is already running.'
    umask 0022
    work_dir=$(mktemp -d /var/tmp/ludus-install.XXXXXXXX)
    release_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
    trap cleanup EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    log "Installing dependencies for Debian $debian_version; building source $source_dir"
    install_dependencies
    install_node
    prepare_accounts
    prepare_storage
    build_release
    activate_release
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
