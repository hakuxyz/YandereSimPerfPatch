#!/usr/bin/env bash
#
# Yandere Simulator Performance Patch
# Finds your game install and patches Assembly-CSharp.dll with YsPerfPatcher.
#
#   ./inject.sh                  search for the game, then patch
#   ./inject.sh /path/to/game    patch this install directly
#   ./inject.sh --update         download the latest version from GitHub, then patch
#   ./inject.sh --restore        put the original DLL back
#   ./inject.sh --help

set -u
set -o pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
patcher="$script_dir/YsPerfPatcher"
repo_api="https://api.github.com/repos/hakuxyz/YandereSimPerfPatch/releases/latest"
repo_download="https://github.com/hakuxyz/YandereSimPerfPatch/releases/latest/download"
version_file="$script_dir/VERSION"

usage() {
    cat <<'EOF'
Yandere Simulator Performance Patch for Linux

  ./inject.sh                  search for the game, then patch it
  ./inject.sh /path/to/game    patch the given game folder
  ./inject.sh --update         fetch the newest version from GitHub, then patch
  ./inject.sh --restore        restore the original Assembly-CSharp.dll
  ./inject.sh --help           show this text

Every normal run also checks GitHub for a newer version and shows a desktop
notification when one is available. The check is skipped when you are offline.

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

local_version() {
    if [ -f "$version_file" ]; then
        tr -d 'vV \r\n' < "$version_file"
    fi
}

latest_version() {
    command -v curl >/dev/null 2>&1 || return 1
    curl -fsSL --max-time 8 "$repo_api" 2>/dev/null |
        sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1 | tr -d 'vV '
}

show_desktop_message() {
    local text="$1"
    printf '%s\n' "$text"
    if command -v notify-send >/dev/null 2>&1; then
        notify-send -a "YandereSimPerfPatch" "Yandere Simulator patch" "$text" \
            >/dev/null 2>&1 || true
    elif command -v zenity >/dev/null 2>&1; then
        zenity --notification --text="$text" >/dev/null 2>&1 || true
    elif command -v kdialog >/dev/null 2>&1; then
        kdialog --title "Yandere Simulator patch" --passivepopup "$text" 10 \
            >/dev/null 2>&1 || true
    fi
}

# Silent during normal runs, skips everything when GitHub cannot be reached.
check_for_update() {
    local current latest
    current=$(local_version)
    [ -n "$current" ] || return 0
    latest=$(latest_version) || return 0
    [ -n "$latest" ] || return 0
    [ "$current" = "$latest" ] && return 0
    # Stay quiet when the local copy is ahead of the latest release.
    [ "$(printf '%s\n' "$current" "$latest" | sort -V | head -n 1)" != "$current" ] && return 0
    show_desktop_message "Your patch is not up to date (installed: $current, latest: $latest). Run ./inject.sh --update to update."
}

do_update() {
    local latest tmp asset url src f
    command -v curl >/dev/null 2>&1 || fail "curl is required for updates"
    latest=$(latest_version) || fail "cannot reach GitHub, check your internet connection"
    [ -n "$latest" ] || fail "no release found on GitHub"

    asset="YandereSimPerfPatch-linux-x64.zip"
    url="$repo_download/$asset"
    tmp=$(mktemp -d) || fail "cannot create a temporary folder"
    trap 'rm -rf "$tmp"' EXIT

    printf '%s\n' "Downloading $asset ($latest)..."
    curl -fL --max-time 300 -o "$tmp/$asset" "$url" ||
        fail "download failed, check your internet connection"

    command -v unzip >/dev/null 2>&1 || fail "unzip is required to extract the update"
    unzip -oq "$tmp/$asset" -d "$tmp/extracted" || fail "could not extract the update"

    src="$tmp/extracted/YandereSimPerfPatch"
    [ -d "$src" ] || fail "unexpected archive layout"

    for f in inject.sh inject.ps1 inject.bat YsPerfPatcher VERSION README.md LICENSE; do
        if [ -e "$src/$f" ]; then
            cp -f "$src/$f" "$script_dir/$f" 2>/dev/null || true
        fi
    done
    if [ -d "$src/docs" ]; then
        mkdir -p "$script_dir/docs"
        cp -rf "$src/docs/." "$script_dir/docs/" 2>/dev/null || true
    fi
    chmod +x "$script_dir/inject.sh" "$script_dir/YsPerfPatcher" 2>/dev/null || true

    trap - EXIT
    rm -rf "$tmp"
    printf '%s\n' "Updated to $latest."
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
    --update)
        do_update
        shift
        ;;
esac

[ -f "$patcher" ] || fail "patcher not found next to this script: $patcher"
[ -x "$patcher" ] || fail "patcher is not executable, run: chmod +x '$patcher'"

check_for_update

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
