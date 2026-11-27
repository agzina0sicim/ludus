#!/bin/sh
set -eu

application_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
cd -- "$application_dir"
if [ -r /etc/ludus/ludus.env ]; then
    set -a
    # shellcheck disable=SC1091
    . /etc/ludus/ludus.env
    set +a
fi

if [ "$(id -u)" -eq 0 ] && id ludus >/dev/null 2>&1; then
    exec /usr/sbin/runuser -u ludus --preserve-environment -- /usr/bin/dotnet "$application_dir/Ludus.Api.dll" "$@"
fi

exec /usr/bin/dotnet "$application_dir/Ludus.Api.dll" "$@"
