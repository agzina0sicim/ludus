#!/bin/sh
set -eu

validate_id() {
    case "$2" in
        ''|*[!0-9]*) printf '%s must be a positive numeric ID.\n' "$1" >&2; exit 1 ;;
    esac
    if [ "${#2}" -gt 10 ] || [ "$2" -eq 0 ] || [ "$2" -gt 2147483647 ]; then
        printf '%s must be between 1 and 2147483647.\n' "$1" >&2
        exit 1
    fi
}

PUID=${PUID:-1000}
PGID=${PGID:-1000}
UMASK=${UMASK:-002}
validate_id PUID "$PUID"
validate_id PGID "$PGID"
case "$UMASK" in
    [0-7][0-7][0-7]) umask "$UMASK" ;;
    *) printf 'UMASK must contain three octal digits.\n' >&2; exit 1 ;;
esac

if [ "${Ludus__ConfigDirectory:-/config}" != /config ]; then
    printf 'The Docker config directory must be /config.\n' >&2
    exit 1
fi
if [ -L /config ] || [ -L /config/keys ]; then
    printf '/config and /config/keys must not be symbolic links.\n' >&2
    exit 1
fi

mkdir -p /config/keys
if [ "$(id -u)" -eq 0 ]; then
    # Do not follow symlinks or cross mounts into download and library storage.
    find /config -xdev -exec chown -h "$PUID:$PGID" {} +
    chmod 750 /config
    chmod 700 /config/keys
    exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups \
        --bounding-set=-all --inh-caps=-all --ambient-caps=-all --no-new-privs "$@"
fi

if [ "$(id -u)" -ne "$PUID" ] || [ "$(id -g)" -ne "$PGID" ] || [ ! -w /config ] || [ ! -w /config/keys ]; then
    printf 'Non-root startup requires matching PUID/PGID and writable config directories.\n' >&2
    exit 1
fi
chmod 700 /config/keys
exec "$@"
