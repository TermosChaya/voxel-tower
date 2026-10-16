#!/bin/zsh
# Runs the game without the editor (builds the C# code first).
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="/opt/homebrew/bin:$PATH"
dotnet build VoxelTower.csproj -nologo -v q || exit 1
exec /Applications/Godot_mono.app/Contents/MacOS/Godot --path . "$@"
