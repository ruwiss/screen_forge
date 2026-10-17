#!/usr/bin/env bash
set -euo pipefail
script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
target="${1:?Usage: ROLLBACK.sh PROJECT_ROOT}"
cp -- "$script_dir/App.xaml.cs.original" "$target/ScreenForge/App.xaml.cs"
cp -- "$script_dir/Program.cs.original" "$target/tools/CaptureProbe/Program.cs"
cmp -- "$script_dir/App.xaml.cs.original" "$target/ScreenForge/App.xaml.cs"
cmp -- "$script_dir/Program.cs.original" "$target/tools/CaptureProbe/Program.cs"
printf "ROLLBACK: App.xaml.cs and Program.cs restored; byte comparison passed\n"
