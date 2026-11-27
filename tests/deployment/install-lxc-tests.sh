#!/usr/bin/env bash
# Literal shell expressions below are intentional hostile-input fixtures.
# shellcheck disable=SC2016
set -Eeuo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
# shellcheck source=install-lxc.sh
source "$repo_root/install-lxc.sh"

defaults() {
    config_dir=/config downloads_dir=/downloads roms_dir=/roms media_dir=''
    service_uid=1000 service_gid=1000 bind_address=127.0.0.1 port=8080 node_version=latest
}

assert_line() { grep -Fxq -- "$1" "$2" || die "Missing line '$1' in $2"; }
assert_absent() { if grep -Fq -- "$1" "$2"; then die "Unexpected '$1' in $2"; fi; }

mock_activation_host() {
    # Every command that could change the host is replaced in the child process.
    # Generated environment/unit/state files stay inside the test workspace.
    systemctl() {
        printf 'systemctl %s\n' "$*" >> "$work_dir/commands"
        if [[ $scenario == enable-failure && $1 == enable ]]; then return 23; fi
        if [[ $scenario == reset-not-loaded && $1 == reset-failed ]]; then return 1; fi
        return 0
    }
    systemd-analyze() {
        printf 'verify %s\n' "$*" >> "$work_dir/commands"
        if [[ $scenario == verify-failure ]]; then return 1; fi
        return 0
    }
    install() { printf 'install %s\n' "$*" >> "$work_dir/commands"; }
    ln() { printf 'ln %s\n' "$*" >> "$work_dir/commands"; }
    mv() { printf 'mv %s\n' "$*" >> "$work_dir/commands"; }
    ss() { if [[ $scenario == occupied-port ]]; then printf 'LISTEN 0 4096 127.0.0.1:8080\n'; fi; }
    curl() { if [[ $scenario == unhealthy ]]; then printf '{"status":"failed"}\n'; else printf '{"status":"ok"}\n'; fi; }
    sleep() { :; }
}

run_scenario() {
    local scenario=$1
    work_dir=$2
    defaults
    config_dir="$work_dir/config"
    mkdir "$config_dir"
    media_dir=/data downloads_dir=/data/downloads roms_dir=/data/roms
    state_file="$work_dir/persisted-state"
    release_id=test-release release_dir=/opt/ludus/releases/test-release rollback_dir=''
    activating=false stopped=false was_running=false
    mock_activation_host
    trap cleanup EXIT
    activate_release
    [[ $activating == false && $stopped == true && $was_running == true ]] || die 'Invalid activation state.'
    assert_line 'ReadWritePaths='"$config_dir"' /data' "$work_dir/ludus.service"
    assert_line 'Restart=always' "$work_dir/ludus.service"
    assert_line 'systemctl enable --now ludus.service' "$work_dir/commands"
    assert_line 'mv -Tf /opt/ludus/.current-test-release /opt/ludus/current' "$work_dir/commands"
}

run_case() {
    local case_name=$1
    shift
    defaults
    case "$case_name" in
        id) validate_id UID "$1" ;;
        path) validate_storage_path "$1" ;;
        overlap) paths_overlap "$1" "$2" ;;
        build-environment)
            node_dir=/opt/ludus/tools/node-test
            local command_log="$1/build-environment"
            exec 9>"$1/install.lock"
            runuser() {
                [[ ! -e /proc/self/fd/9 ]] || die 'Build inherited the installer lock.'
                printf '%s\n' "$@" > "$command_log"
            }
            run_build_command "$1/build" dotnet --version
            [[ -e /proc/self/fd/9 ]] || die 'Installer lost its lock descriptor.'
            assert_line 'ludus-build' "$command_log"
            assert_line "HOME=$1/build/home" "$command_log"
            assert_line "DOTNET_CLI_HOME=$1/build/dotnet" "$command_log"
            assert_line "TMPDIR=$1/build/tmp" "$command_log"
            assert_line "XDG_RUNTIME_DIR=$1/build/runtime" "$command_log"
            assert_line "XDG_CONFIG_HOME=$1/build/home/.config" "$command_log"
            assert_line "XDG_DATA_HOME=$1/build/home/.local/share" "$command_log"
            assert_line "XDG_CACHE_HOME=$1/build/cache" "$command_log"
            assert_line "NUGET_PACKAGES=$1/build/cache/nuget" "$command_log"
            assert_line "npm_config_cache=$1/build/cache/npm" "$command_log"
            assert_line 'dotnet' "$command_log"
            assert_line '--version' "$command_log"
            ;;
        build-command-failure)
            node_dir=/opt/ludus/tools/node-test
            runuser() { return 17; }
            run_build_command "$1/build" dotnet --version
            ;;
        options)
            case "$1" in
                port) port=$2 ;; bind) bind_address=$2 ;; node) node_version=$2 ;;
                config) config_dir=$2 ;; downloads) downloads_dir=$2 ;; roms) roms_dir=$2 ;;
                media) media_dir=$2; downloads_dir="$2/downloads"; roms_dir="$2/roms" ;;
                outside-media) media_dir=/data ;;
                media-config) media_dir=/data; config_dir=/data/config; downloads_dir=/data/downloads; roms_dir=/data/roms ;;
                *) die "Unknown option test: $1" ;;
            esac
            validate_options
            ;;
        render)
            render_service > "$1/unit"
            assert_line 'ReadWritePaths=/config /downloads /roms' "$1/unit"
            assert_line 'Restart=always' "$1/unit"
            assert_line 'NoNewPrivileges=true' "$1/unit"
            assert_line 'CapabilityBoundingSet=' "$1/unit"
            assert_absent 'MemoryDenyWriteExecute' "$1/unit"
            media_dir=/data downloads_dir=/data/downloads roms_dir=/data/roms
            render_service > "$1/unit"
            assert_line 'ReadWritePaths=/config /data' "$1/unit"
            assert_line 'RequiresMountsFor=/config /data/downloads /data/roms' "$1/unit"
            render_environment > "$1/env"
            assert_line 'ASPNETCORE_URLS=http://127.0.0.1:8080' "$1/env"
            assert_line 'Ludus__DownloadDirectory=/data/downloads' "$1/env"
            save_state > "$1/state"
            assert_line 'MEDIA_DIR=/data' "$1/state"
            ;;
        state)
            state_file="$2/state"
            save_state > "$state_file"
            # stat is the only privileged-file metadata dependency of read_state.
            stat() { if [[ $2 == %u ]]; then printf '%s\n' "${test_owner:-0}"; else printf '%s\n' "${test_mode:-600}"; fi; }
            case "$1" in
                roundtrip) config_dir=/changed; read_state; [[ $config_dir == /config ]] ;;
                owner) test_owner=1000; read_state ;;
                mode) test_mode=644; read_state ;;
                unknown) printf 'COMMAND=touch sentinel\n' >> "$state_file"; read_state ;;
                injection)
                    printf 'CONFIG_DIR=$(touch "%s/sentinel")\n' "$2" >> "$state_file"
                    read_state
                    [[ ! -e $2/sentinel ]] || die 'State contents were executed.'
                    validate_options
                    ;;
                *) die "Unknown state test: $1" ;;
            esac
            ;;
        *) die "Unknown test case: $case_name" ;;
    esac
}

if [[ ${1:-} == --case ]]; then shift; run_case "$@"; exit; fi
if [[ ${1:-} == --scenario ]]; then shift; run_scenario "$@"; exit; fi

[[ -d $repo_root/.local ]] || mkdir "$repo_root/.local"
test_root=$(mktemp -d "$repo_root/.local/lxc-installer-tests.XXXXXXXX")
remove_test_files() {
    local resolved
    resolved=$(readlink -m -- "$test_root")
    if [[ $resolved == "$repo_root/.local/lxc-installer-tests."* && $resolved == "$test_root" && ! -L $test_root ]]; then
        rm -rf -- "$test_root"
    else
        printf 'Refusing to remove unexpected test path: %s\n' "$test_root" >&2
    fi
}
trap remove_test_files EXIT

passed=0
expect() {
    local expected=$1 description=$2 actual=0
    shift 2
    "$BASH" "${BASH_SOURCE[0]}" "$@" > "$test_root/output" 2>&1 || actual=$?
    if [[ $expected == success && $actual != 0 ]] || [[ $expected == failure && $actual == 0 ]]; then
        cat "$test_root/output" >&2
        die "FAIL: $description (exit $actual)"
    fi
    passed=$((passed + 1))
    printf 'PASS: %s\n' "$description"
}

for value in 1 1000 2147483647; do expect success "accept UID $value" --case id "$value"; done
for value in 0 -1 01 2147483648 999999999999 '1+1' 'a[$(touch sentinel)]'; do expect failure "reject UID $value" --case id "$value"; done
for value in /config /data/downloads /srv/ludus-roms /var/lib/ludus; do expect success "accept path $value" --case path "$value"; done
for value in / /etc/ludus /usr/share /opt/ludus/releases /root/media /tmp/media /data/../etc /data/ /data//roms '/data/my roms' '/data/%n' '/data/$HOME'; do
    expect failure "reject path $value" --case path "$value"
done
expect success 'detect equal paths' --case overlap /data /data
expect success 'detect nested paths' --case overlap /data /data/roms
expect failure 'respect directory boundary' --case overlap /data/roms /data/roms2
for value in 1024 8080 65535; do expect success "accept port $value" --case options port "$value"; done
for value in 1023 65536 08080 '8080;false'; do expect failure "reject port $value" --case options port "$value"; done
expect success 'accept LAN bind' --case options bind 0.0.0.0
expect failure 'reject unexpected bind address' --case options bind 192.168.1.2
expect success 'accept pinned Node 24' --case options node 24.22.3
expect failure 'reject other Node major' --case options node 22.1.0
expect failure 'reject config inside downloads' --case options config /downloads/config
expect failure 'reject equal download and ROM roots' --case options downloads /roms
expect success 'accept common media root' --case options media /data
expect failure 'reject directories outside media root' --case options outside-media ''
expect failure 'reject config inside media root' --case options media-config ''
expect success 'render hardened service and shared media mount' --case render "$test_root"
expect success 'give the build account private home, XDG and temp paths' --case build-environment "$test_root"
expect failure 'propagate failure from the build account command' --case build-command-failure "$test_root"
expect success 'round-trip installer state' --case state roundtrip "$test_root"
expect failure 'reject untrusted state owner' --case state owner "$test_root"
expect failure 'reject world-readable state' --case state mode "$test_root"
expect failure 'reject unknown state fields' --case state unknown "$test_root"
expect failure 'reject shell syntax in state without executing it' --case state injection "$test_root"
[[ ! -e $test_root/sentinel ]] || die 'Unsafe state execution.'

for scenario in healthy unhealthy occupied-port enable-failure verify-failure reset-not-loaded; do
    mkdir "$test_root/$scenario"
    outcome=failure
    if [[ $scenario == healthy || $scenario == reset-not-loaded ]]; then outcome=success; fi
    expect "$outcome" "activation: $scenario" --scenario "$scenario" "$test_root/$scenario"
done
assert_line 'systemctl stop ludus.service' "$test_root/unhealthy/commands"
[[ $(grep -Fc 'systemctl stop ludus.service' "$test_root/unhealthy/commands") == 2 ]] || die 'Unhealthy candidate was not stopped.'
assert_absent 'systemctl start ludus.service' "$test_root/unhealthy/commands"
assert_line 'systemctl start ludus.service' "$test_root/occupied-port/commands"
assert_absent 'systemctl enable' "$test_root/occupied-port/commands"
assert_absent 'mv -Tf' "$test_root/occupied-port/commands"
for scenario in enable-failure verify-failure; do
    [[ $(grep -Fc 'systemctl stop ludus.service' "$test_root/$scenario/commands") == 2 ]] || die "Failure in $scenario did not stop the service."
    assert_absent 'systemctl start ludus.service' "$test_root/$scenario/commands"
done
assert_absent 'systemctl enable' "$test_root/verify-failure/commands"
assert_absent 'mv -Tf' "$test_root/verify-failure/commands"
printf '\n%d installer checks passed; host commands were mocked.\n' "$passed"
