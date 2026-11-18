---
name: repo-conventions
description: >
  Single source of truth for branch naming, commit message format and PR
  title/body conventions used by every /issue-to-pr subagent (Architect/Planner,
  C# Implementer, QA/Self-Healing, Release/PR). Subagents reference this file
  by name instead of duplicating the rules in their own prompts.
---

# Repository Conventions

This file is read by every subagent in the `/issue-to-pr` pipeline before it
performs any action that creates a branch, a commit, or a pull request. It is
the only place these conventions are defined — if a convention changes, it
changes here only.

## 1. Branch naming

Branches created by the `/issue-to-pr` pipeline follow this pattern:

```text
feat/t3-<issueNumber>-<slug>
```

- `t3` is a fixed literal segment identifying branches produced by this
  pipeline (Team 4 / Task 3 agent track), so they are visually distinguishable
  from manually-created branches in this repository, which historically use
  `feature/0VP-T4-<issueNumber>-<slug>` (see e.g. `feature/0VP-T4-11-my-tasks-weekly-user-id`).
- `<issueNumber>` is the bare GitHub issue number (no `#`, no leading zeros).
- `<slug>` is derived from the issue title:
  1. lowercase the title;
  2. replace any run of characters that are not `[a-z0-9]` with a single `-`;
  3. strip leading/trailing `-`;
  4. truncate to at most 40 characters, then strip a trailing `-` left by
     truncation.
- Example: issue `#21`, title *"Implement Issue Ingestion & Git Branching
  Agent"* → slug `implement-issue-ingestion-git-branching` (the full slug
  `implement-issue-ingestion-git-branching-agent` is cut to 40 characters, and
  the trailing `-` left by the cut is stripped) →
  `feat/t3-21-implement-issue-ingestion-git-branching`.

## 2. Commit message format

Conventional-commits style, one type prefix per commit, imperative mood,
lowercase after the colon:

| Prefix  | Use for |
|---------|---------|
| `feat:` | New functionality (API endpoints, agents, scripts, infra). |
| `test:` | Adding or changing tests only. |
| `fix:`  | Bug fixes, including fixes produced by the QA/Self-Healing stage. |
| `docs:` | Documentation only (`docs/`, `README.md`, rule/prompt files). |

Optionally reference the issue number in parentheses at the end of the
subject line, e.g.:

```text
feat: add read-only MCP server for todo queries (#14)
```

Subject line ≤ 72 characters. Body (optional, blank line after subject) may
explain *why*, not *what* — the diff already shows what changed.

## 3. Pull request title & body conventions

- **Title:** same subject line as the primary commit (type prefix + imperative
  summary), with the issue number in parentheses: `feat: add weekly repeat
  endpoint (#12)`.
- **Body** must contain, in this order:
  1. `Closes #<issueNumber>` (or `Fixes #<issueNumber>` for bug-fix issues) on
     its own line, so GitHub auto-links and auto-closes the issue on merge.
  2. A short **Summary** section (2–5 sentences) describing what changed and
     why, written for a human reviewer, not restating the issue body verbatim.
  3. A **Testing** section listing how the change was verified (commands run,
     test names, manual checks).
  4. Any relevant notes on scope limits, follow-ups, or known limitations.

## 4. Who must follow this file

Every subagent under `skills/issue-to-pr/agents/` (Architect/Planner,
C# Implementer, QA/Self-Healing, Release/PR) reads this file before creating
branches, commits, or pull requests, and never defines its own, divergent
version of these conventions. If a subagent needs a convention this file
doesn't cover, add it here rather than inventing a local rule.
