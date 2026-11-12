#!/usr/bin/env bash
# Copies skills/<name>/ to .claude/skills/<name>/ so Claude Code discovers the skill.
# The source of truth stays in skills/ - never edit the copy by hand.
#
#   bash scripts/sync-claude-skills.sh              # sync issue-to-pr
#   bash scripts/sync-claude-skills.sh --check      # change nothing, exit 1 if out of sync
#   bash scripts/sync-claude-skills.sh other-skill  # sync another skill
set -euo pipefail

SKILL="issue-to-pr"
CHECK=0
for arg in "$@"; do
  case "$arg" in
    --check) CHECK=1 ;;
    *) SKILL="$arg" ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$REPO_ROOT/skills/$SKILL"
TARGET="$REPO_ROOT/.claude/skills/$SKILL"

if [[ ! -d "$SOURCE" ]]; then
  echo "Source skill not found: $SOURCE" >&2
  exit 1
fi

if (( CHECK )); then
  if diff -r "$SOURCE" "$TARGET" > /dev/null 2>&1; then
    echo "In sync: $TARGET"
    exit 0
  fi
  echo "Out of sync: $TARGET. Run scripts/sync-claude-skills.sh" >&2
  exit 1
fi

rm -rf "$TARGET"
mkdir -p "$TARGET"
cp -R "$SOURCE/." "$TARGET/"
echo "Synced $SOURCE -> $TARGET"