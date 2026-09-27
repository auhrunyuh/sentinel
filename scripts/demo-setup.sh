#!/bin/sh
# ponytail: no arg parsing lib — two positional args is all this needs.
set -e

NN="$1"
if [ -z "$NN" ]; then
  echo "usage: demo-setup.sh <NN> [dir]" >&2
  exit 1
fi

ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
SRC="$ROOT/samples/OrderService"
DIR="${2:-${TMPDIR:-/tmp}/sentinel-demo-$NN}"

PATCH="$SRC/bugs/$NN.patch"
ALERT="$SRC/alerts/$NN.json"
if [ ! -f "$PATCH" ] || [ ! -f "$ALERT" ]; then
  echo "no bug/alert $NN under $SRC" >&2
  exit 1
fi

rm -rf "$DIR"
mkdir -p "$DIR"

# copy sample app, excluding bugs/ and hidden-tests/ (agent must never see them), keep alerts/
( cd "$SRC" && tar cf - --exclude=bugs --exclude=hidden-tests . ) | ( cd "$DIR" && tar xf - )

cp -R "$ROOT/.bob" "$DIR/.bob"
mkdir -p "$DIR/scripts"
cp "$ROOT/scripts/sentinel" "$DIR/scripts/sentinel"

cat > "$DIR/.bob/settings.json" <<EOF
{
  "hooks": {
    "PreToolUse": [
      { "hooks": [{ "type": "command", "command": "SENTINEL_HOME=$ROOT sh scripts/sentinel hook pre-tool", "timeout": 10 }] }
    ],
    "PostToolUse": [
      { "hooks": [{ "type": "command", "command": "SENTINEL_HOME=$ROOT sh scripts/sentinel hook post-tool", "timeout": 10 }] }
    ],
    "Stop": [
      { "hooks": [{ "type": "command", "command": "SENTINEL_HOME=$ROOT sh scripts/sentinel hook stop", "timeout": 600 }] }
    ]
  }
}
EOF

cat > "$DIR/.gitignore" <<'EOF'
bin/
obj/
.sentinel/
SENTINEL_REPORT.md
EOF

( cd "$DIR" && git init -q && git add -A && git commit -q -m "clean" )
( cd "$DIR" && git apply "$PATCH" && git add -A && git commit -q -m "inject bug $NN" )

( cd "$DIR" && dotnet test --nologo -v quiet ) || {
  echo "dotnet test failed after injecting bug $NN — bug should escape visible tests" >&2
  exit 1
}

echo ""
echo "demo ready at $DIR (bug $NN injected, dotnet test green — bug escapes visible tests)"
echo "next:"
echo "  cd $DIR && SENTINEL_HOME=$ROOT sh scripts/sentinel fix --alert alerts/$NN.json --project OrderService.slnx --max-cost 1"
