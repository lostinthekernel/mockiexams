#!/bin/zsh
# Builds the Windows app and its installer from macOS.
# Needs: the .NET SDK (~/.dotnet), and CrossOver with Inno Setup 6 in a bottle named mockiexams-build.
# Output: build/MockiExams-<version>-Windows-{x64,arm64}-Setup.exe
set -euo pipefail

ROOT="${0:A:h:h}"
VERSION="${1:-1.0}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
CX="/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin"
BOTTLE="mockiexams-build"
ISCC='C:\InnoSetup\ISCC.exe'

dotnet test "$ROOT/windows/tests/MockiCore.Tests" -v q --nologo | tail -1

for ARCH in x64 arm64; do
  OUT="$ROOT/build/win-$ARCH"
  rm -rf "$OUT"
  dotnet publish "$ROOT/windows/src/MockiExams" -c Release -r "win-$ARCH" --self-contained --nologo -v q \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -p:Version="$VERSION.0" \
    -o "$OUT"

  # The assemblies inside the exe must not carry local paths or names.
  OBJ="$ROOT/windows/src/MockiExams/obj/Release/net10.0/win-$ARCH"
  if grep -qaiF -e "$USER" -e "/Users/" "$OBJ/MockiExams.dll" "$ROOT/windows/src/MockiCore/obj/Release/net10.0/MockiCore.dll"; then
    echo "Personal strings found in the build; not packaging." >&2
    exit 1
  fi

  "$CX/wine" --bottle "$BOTTLE" --wait-children "$ISCC" /Q "/DAppVersion=$VERSION" "/DArch=$ARCH" "Z:${ROOT//\//\\}\\windows\\installer\\MockiExams.iss"
  SETUP="$ROOT/build/MockiExams-$VERSION-Windows-$ARCH-Setup.exe"
  ls -la "$SETUP"
  shasum -a 256 "$SETUP"
done
