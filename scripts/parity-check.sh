#!/bin/zsh
# Checks that the Swift core (macOS) and the C# core (Windows) agree: the same validation issues,
# the same converter output, the same stats and templates. Any difference is printed and fails.
# Usage: scripts/parity-check.sh
set -uo pipefail

ROOT="${0:A:h:h}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

swift build -q -c release --package-path "$ROOT/Packages/MockiCore" --product mockiexam || exit 1
SWIFT_BIN="$(swift build -c release --package-path "$ROOT/Packages/MockiCore" --show-bin-path)/mockiexam"
dotnet build -c Release -v q --nologo "$ROOT/windows/src/mockiexam" -o "$WORK/cs" >/dev/null || exit 1
CS_BIN="$WORK/cs/mockiexam"
# Pin both to one locale; Foundation reads the Mac region, .NET reads LANG.
SWIFT() { "$SWIFT_BIN" "$@" -AppleLocale en_US; }
CS() { LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8 "$CS_BIN" "$@"; }

failures=0
same() { # label, file-a, file-b
  if ! cmp -s "$2" "$3"; then
    echo "DIFF: $1"; diff "$2" "$3" | head -20; failures=$((failures + 1))
  fi
}

# Validation: every sample, every template, and the broken fixtures.
inputs=("$ROOT/samples/sample.exam.json" "$ROOT/samples/networking-sim" "$ROOT"/windows/tests/parity/*.json)
for kind in single multiple trueFalse yesNo ordering matching dropdowns fillIn hotspot exam; do
  SWIFT template $kind > "$WORK/t-$kind.txt"; CS template $kind > "$WORK/c-$kind.txt"
  same "template $kind" "$WORK/t-$kind.txt" "$WORK/c-$kind.txt"
  [[ $kind == exam ]] && cp "$WORK/t-exam.txt" "$WORK/template-exam.json" && inputs+=("$WORK/template-exam.json")
done
SWIFT schema > "$WORK/s-schema"; CS schema > "$WORK/c-schema"; same "schema" "$WORK/s-schema" "$WORK/c-schema"

for input in "${inputs[@]}"; do
  name="${input:t}"
  SWIFT validate "$input" --json > "$WORK/s.json" 2>/dev/null
  CS validate "$input" --json > "$WORK/c.json" 2>/dev/null
  python3 -I - "$WORK/s.json" "$WORK/c.json" "$name" <<'EOF' || failures=$((failures + 1))
import json, sys
def load(p):
    rows = json.load(open(p))
    for r in rows:
        r.pop("file", None)
        for i in r["issues"]:
            # Decoder wording differs between Foundation and System.Text.Json; the path must still match.
            for prefix in ("Wrong type", "Not valid JSON"):
                if i["message"].startswith(prefix): i["message"] = prefix
    return rows
a, b = load(sys.argv[1]), load(sys.argv[2])
if a != b:
    print("DIFF: validate", sys.argv[3])
    print("  swift:", json.dumps(a)[:900])
    print("  c#:   ", json.dumps(b)[:900])
    sys.exit(1)
EOF
done

# Conversion: byte-for-byte identical exam JSON and the same skipped list.
for src in "$ROOT"/samples/converters/*; do
  name="${src:t}"
  SWIFT convert "$src" --id conv --title "Converted" > "$WORK/s-conv" 2> "$WORK/s-err"
  CS convert "$src" --id conv --title "Converted" > "$WORK/c-conv" 2> "$WORK/c-err"
  same "convert $name" "$WORK/s-conv" "$WORK/c-conv"
  same "convert $name (messages)" <(sed "s#$src##" "$WORK/s-err") <(sed "s#$src##" "$WORK/c-err")
done

for input in "$ROOT/samples/sample.exam.json" "$ROOT/samples/networking-sim"; do
  SWIFT stats "$input" > "$WORK/s-stats"; CS stats "$input" > "$WORK/c-stats"
  same "stats ${input:t}" "$WORK/s-stats" "$WORK/c-stats"
done

if (( failures )); then echo "parity: $failures difference(s)"; exit 1; fi
echo "parity: OK (${#inputs[@]} exams validated, $(ls "$ROOT"/samples/converters | wc -l | tr -d ' ') converter inputs, templates, schema, stats)"
