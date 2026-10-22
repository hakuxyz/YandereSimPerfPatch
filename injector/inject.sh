#!/usr/bin/env bash
#
# Yandere Simulator Performance Patch
# Finds your game install and patches Assembly-CSharp.dll with YsPerfPatcher.
#
#   ./inject.sh                  search for the game, then patch
#   ./inject.sh /path/to/game    patch this install directly
#   ./inject.sh --restore        put the original DLL back
#   ./inject.sh --help

set -u
set -o pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
patcher="$script_dir/YsPerfPatcher"

usage() {
    cat <<'EOF'
Yandere Simulator Performance Patch for Linux

  ./inject.sh                  search for the game, then patch it
  ./inject.sh /path/to/game    patch the given game folder
  ./inject.sh --restore        restore the original Assembly-CSharp.dll
  ./inject.sh --help           show this text

The game folder is the one that contains YandereSimulator.exe.
You can also point at the DLL directly:

  ./inject.sh "/path/YandereSimulator_Data/Managed/Assembly-CSharp.dll"

Environment override for the search:

  YANDERE_SIM_PATH=/path/to/game ./inject.sh
EOF
}

fail() {
    printf '%s\n' "error: $*" >&2
    exit 1
}

# Accepts a folder or the DLL path, prints the DLL path if it is valid.
resolve_dll() {
    local input="$1"
    if [ -d "$input" ]; then
        if [ -f "$input/YandereSimulator_Data/Managed/Assembly-CSharp.dll" ]; then
            printf '%s\n' "$input/YandereSimulator_Data/Managed/Assembly-CSharp.dll"
            return 0
        fi
        return 1
    fi
    if [ -f "$input" ]; then
        case "$input" in
            */YandereSimulator_Data/Managed/Assembly-CSharp.dll) printf '%s\n' "$input"; return 0 ;;
        esac
    fi
    return 1
}

# Prints candidate DLL paths, one per line, in order of confidence.
collect_candidates() {
    local dirs=(
        "${YANDERE_SIM_PATH:-}"
        "${YANDERE_LAUNCHER_INSTALL_DIR:-}"
        "$script_dir"
        "$script_dir/YandereSimulator"
        "$HOME/Games/YandereSimulator"
        "$HOME/YandereSimulator"
        "$HOME/.wine/drive_c/YandereSimulator"
        "$HOME/.wine/drive_c/Games/YandereSimulator"
        "$HOME/Downloads/YandereSimulator"
        "$HOME/Downloads/YandereSimulator/YandereSimulator"
        "$HOME/Desktop/YandereSimulator"
    )
    local d dll
    for d in "${dirs[@]}"; do
        [ -n "$d" ] || continue
        dll=$(resolve_dll "$d" 2>/dev/null) && printf '%s\n' "$dll"
    done

    local wine_users
    for wine_users in "$HOME"/.wine/drive_c/users/*/Desktop/YandereSimulator; do
        [ -d "$wine_users" ] || continue
        dll=$(resolve_dll "$wine_users" 2>/dev/null) && printf '%s\n' "$dll"
    done
}

do_restore() {
    local dll="$1"
    local orig="$dll.orig"
    if [ ! -f "$orig" ]; then
        fail "no backup next to the DLL, nothing to restore: $orig"
    fi
    cp -- "$orig" "$dll" || fail "could not write $dll"
    printf '%s\n' "Restored the original Assembly-CSharp.dll."
}

patch_dll() {
    local dll="$1"
    local out rc
    out=$("$patcher" "$dll" 2>&1)
    rc=$?
    printf '%s\n' "$out"

    case $rc in
        0)
            printf '%s\n' "Patched: $dll"
            ;;
        2)
            printf '%s\n' "Already patched, nothing to do: $dll"
            ;;
        3)
            fail "the patcher stopped before touching the file, see the message above"
            ;;
        *)
            fail "patcher failed with exit code $rc"
            ;;
    esac
}

# ---------------------------------------------------------------------------

case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    --restore)
        if [ $# -lt 2 ]; then
            dll=$(collect_candidates | head -n 1)
            [ -n "$dll" ] || fail "game not found, pass the path: ./inject.sh --restore /path/to/game"
        else
            dll=$(resolve_dll "$2") || fail "no Assembly-CSharp.dll under: $2"
        fi
        do_restore "$dll"
        exit 0
        ;;
esac

[ -f "$patcher" ] || fail "patcher not found next to this script: $patcher"
[ -x "$patcher" ] || fail "patcher is not executable, run: chmod +x '$patcher'"

target=""
if [ $# -ge 1 ]; then
    target=$(resolve_dll "$1") || fail "no Assembly-CSharp.dll under: $1"
else
    mapfile -t found < <(collect_candidates | awk '!seen[$0]++')
    if [ "${#found[@]}" -eq 0 ]; then
        printf '%s\n' "Could not find the game. Tried these locations:" >&2
        printf '%s\n' "  Home/Games, Home/YandereSimulator, Downloads, Desktop," >&2
        printf '%s\n' "  the Wine drive_c, and YandereSimulator folders next to this script." >&2
        printf '%s\n' "" >&2
        printf '%s\n' "Run it with a path instead:" >&2
        printf '%s\n' "  ./inject.sh /path/to/YandereSimulator" >&2
        printf '%s\n' "" >&2
        printf '%s\n' "or set YANDERE_SIM_PATH=/path/to/YandereSimulator" >&2
        exit 1
    fi
    if [ "${#found[@]}" -gt 1 ]; then
        printf '%s\n' "Found more than one install:" >&2
        local_i=1
        for f in "${found[@]}"; do
            printf '  %d) %s\n' "$local_i" "$f" >&2
            local_i=$((local_i + 1))
        done
        printf '%s\n' "Pick one by passing its folder:" >&2
        printf '%s\n' "  ./inject.sh /path/from/the/list" >&2
        exit 1
    fi
    target="${found[0]}"
    printf '%s\n' "Found game DLL: $target"
fi

patch_dll "$target"
