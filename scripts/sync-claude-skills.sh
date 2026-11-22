#!/usr/bin/env bash
# Copies skills/<name>/ to .claude/skills/<name>/ so Claude Code discovers the skill,
# and registers the skill's subagents (skills/<name>/agents/*.md) under .claude/agents/.
# The source of truth stays in skills/ - never edit the copies by hand.
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
AGENTS_SOURCE="$SOURCE/agents"
AGENTS_TARGET="$REPO_ROOT/.claude/agents"

if [[ ! -d "$SOURCE" ]]; then
  echo "Source skill not found: $SOURCE" >&2
  exit 1
fi

if (( CHECK )); then
  status=0

  # 1) The whole skill directory must match its generated copy.
  if ! diff -r "$SOURCE" "$TARGET" > /dev/null 2>&1; then
    echo "Out of sync: $TARGET" >&2
    status=1
  fi

  # 2) Every subagent must be registered under .claude/agents/ with identical content.
  if [[ -d "$AGENTS_SOURCE" ]]; then
    for file in "$AGENTS_SOURCE"/*.md; do
      [[ -e "$file" ]] || continue
      name="$(basename "$file")"
      if ! cmp -s "$file" "$AGENTS_TARGET/$name"; then
        echo "Out of sync: $AGENTS_TARGET/$name" >&2
        status=1
      fi
    done
  fi

  if (( status == 0 )); then
    echo "In sync: $TARGET and $AGENTS_TARGET"
    exit 0
  fi
  echo "Run: bash scripts/sync-claude-skills.sh" >&2
  exit 1
fi

# Skill copy: recreated from scratch so files deleted in skills/ disappear too.
rm -rf "$TARGET"
mkdir -p "$TARGET"
cp -R "$SOURCE/." "$TARGET/"
echo "Synced $SOURCE -> $TARGET"

# Subagent registration: .claude/agents/ is shared, so only the files that exist in
# skills/<name>/agents/ are copied (other agents in that directory are left alone).
if [[ -d "$AGENTS_SOURCE" ]]; then
  mkdir -p "$AGENTS_TARGET"
  for file in "$AGENTS_SOURCE"/*.md; do
    [[ -e "$file" ]] || continue
    cp "$file" "$AGENTS_TARGET/"
    echo "Registered agent: $AGENTS_TARGET/$(basename "$file")"
  done
fi
