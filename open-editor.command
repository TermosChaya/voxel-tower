#!/bin/zsh
# Opens the project in the Godot editor. Godot looks for .NET in /usr/local/share/dotnet;
# here .NET 10 comes from Homebrew, so point it there.
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="/opt/homebrew/bin:$PATH"
exec /Applications/Godot_mono.app/Contents/MacOS/Godot --path . -e
