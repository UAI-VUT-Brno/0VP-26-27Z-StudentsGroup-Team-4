---
name: issue-to-pr
description: >
  Orchestrates the end-to-end "issue to pull request" workflow for this repository.
  Triggered by the /issue-to-pr slash command. Parses and validates the GitHub issue
  reference supplied by the user, then routes control sequentially through the
  specialized subagents (Architect/Planner -> C# Implementer -> QA/Self-Healing ->
  Release/PR) until a pull request is opened or the pipeline halts with a reported
  error. This file owns routing and input validation only; each subagent's internal
  prompt, scoped tool list and acceptance logic is defined in its own issue
  (see Issues 2-5) and lives under agents/.
---

# /issue-to-pr — Orchestrator Skill

## 1. Purpose

This skill is the single entry point for turning a GitHub issue in
`UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4` into an opened, reviewable pull
request, with no manual hand-off between stages. It does not implement any
stage's logic itself — it **parses input, validates it, and hands control to
subagents in a fixed sequence**.

This file is intentionally the only "always loaded" document for the command.
Everything stage-specific (subagent system prompts, few-shot prompts, rule
files) is loaded lazily by the relevant subagent, not by the orchestrator.

## 2. Directory layout

```text
skills/issue-to-pr/
├── SKILL.md          # this file — orchestrator & routing logic (Issue 1)
├── prompts/           # shared / reusable prompt fragments (populated in later issues)
├── agents/             # one file per subagent: system prompt + scoped tool list
│                       #   architect-planner.md      (Issue 2)
│                       #   csharp-implementer.md     (Issue 3)
│                       #   qa-self-healing.md         (Issue 4)
│                       #   release-pr.md               (Issue 5)
└── rules/              # cross-cutting rules all subagents must respect
                        # (coding standards, commit/PR conventions, repo-specific
                        # constraints such as UTF-8-BOM, see .editorconfig)
```

`prompts/`, `agents/` and `rules/` are intentionally empty for this issue
(tracked via `.gitkeep`). They are populated by Issues 2–5 without requiring
any further change to this file's routing logic.

## 3. Command syntax

```text
/issue-to-pr <issue-reference>
```

`<issue-reference>` MUST be one of the following forms. The orchestrator
accepts all three and normalizes them to a single integer `issueNumber`
before routing begins.

| Form                | Example                                                                              |
|---------------------|---------------------------------------------------------------------------------------|
| Bare number          | `42`                                                                                   |
| Hash-prefixed         | `#42`                                                                                  |
| Markdown link         | `[#42](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/pull/42)`       |
| Full GitHub URL       | `https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/issues/42`           |

> Note on the markdown-link form: the *display text* may say `#42` while the
> *link target* points at `/pull/42` (GitHub renders issue/PR cross-references
> this way once a PR exists). The orchestrator always trusts the numeric ID —
> whichever source it is extracted from — and never relies on the link text
> alone, since display text and target can in principle diverge.

## 4. Input parsing & validation algorithm

Applied in this order; the first matching rule wins.

1. **Strip whitespace** from the raw argument string.
2. **Markdown link form** — if the argument matches
   `\[#?\d+\]\((https?:\/\/[^\)]+)\)`, extract the URL inside `()` and
   continue to step 3 using that URL as input (the bracketed label is
   discarded — see note above).
3. **Full URL form** — if the argument matches
   `^https?:\/\/github\.com\/([^\/\s]+)\/([^\/\s]+)\/(issues|pull)\/(\d+)\/?.*$`:
   - capture group 1/2 as `owner`/`repo` and compare against the configured
     repository (`UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4`); on mismatch,
     fail with `ERR_WRONG_REPO` (see §6) instead of silently proceeding;
   - capture group 4 as `issueNumber`.
4. **Hash-prefixed form** — if the argument matches `^#(\d+)$`, capture
   group 1 as `issueNumber`.
5. **Bare number form** — if the argument matches `^(\d+)$`, use it directly
   as `issueNumber`.
6. If none of the above match, or `issueNumber` is not a positive integer,
   fail with `ERR_INVALID_REFERENCE` (see §6). Do not guess or fall back to
   a default issue.
7. On success, confirm the issue exists and is open via the GitHub tool
   available to the orchestrator (read-only lookup) before routing to Stage
   1. If the issue cannot be found, fail with `ERR_ISSUE_NOT_FOUND`. If it is
   already closed, warn the user and ask for explicit confirmation before
   proceeding (do not refuse outright — a closed issue may legitimately be
   reopened as part of the workflow).

### Reference parsing examples

| Input                                                                                  | Parsed `issueNumber` |
|------------------------------------------------------------------------------------------|:---:|
| `42`                                                                                     | `42` |
| `#42`                                                                                     | `42` |
| `[#42](https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/pull/42)`           | `42` |
| `https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/issues/42`                | `42` |
| `https://github.com/some-other-org/other-repo/issues/42`                                   | fails: `ERR_WRONG_REPO` |
| `issue 42` / `#abc` / empty string                                                          | fails: `ERR_INVALID_REFERENCE` |

## 5. Stage pipeline & subagent ownership

The orchestrator hands off control **sequentially**. Each stage must
complete (or be explicitly skipped per its own rules) before the next stage
starts. The orchestrator does not run stages in parallel and does not let a
later stage start if an earlier one reported a hard failure.

| # | Stage               | Owning subagent           | Defined in | Responsibility (high level)                                                                 |
|---|----------------------|----------------------------|------------|-----------------------------------------------------------------------------------------------|
| 1 | Plan                 | **Architect / Planner**     | Issue 2 — `agents/architect-planner.md`     | Reads the issue, produces an implementation plan (affected projects/files, approach, risks). |
| 2 | Implement            | **C# Implementer**           | Issue 3 — `agents/csharp-implementer.md`     | Turns the plan into code changes against `src/` (and matching tests under `tests/`).         |
| 3 | Verify & self-heal    | **QA / Self-Healing**         | Issue 4 — `agents/qa-self-healing.md`         | Builds, runs tests/lint, attempts bounded automatic fixes on failure, re-verifies.           |
| 4 | Release               | **Release / PR**              | Issue 5 — `agents/release-pr.md`               | Commits, pushes a branch, opens the pull request, links it back to the source issue.         |

Handoff contract between stages (minimum, refined per-subagent in Issues
2–5): each subagent receives the `issueNumber`, the repository context, and
the previous stage's structured output; it returns a structured result plus
a `status` of `ok` or `failed`. On `failed`, the orchestrator stops the
pipeline, surfaces the subagent's reported reason to the user, and does not
attempt the next stage automatically.

```text
/issue-to-pr <ref>
     │
     ▼
 [Orchestrator: parse + validate issueNumber]
     │
     ▼
 Stage 1 — Architect/Planner   ──status: failed──▶ STOP, report to user
     │ ok
     ▼
 Stage 2 — C# Implementer      ──status: failed──▶ STOP, report to user
     │ ok
     ▼
 Stage 3 — QA/Self-Healing     ──status: failed──▶ STOP, report to user
     │ ok
     ▼
 Stage 4 — Release/PR          ──status: failed──▶ STOP, report to user
     │ ok
     ▼
 PR opened, linked to issue #<issueNumber>
```

## 6. Error handling (orchestrator-level)

| Code                  | Meaning                                              | Orchestrator behavior |
|------------------------|--------------------------------------------------------|--------------------------|
| `ERR_INVALID_REFERENCE` | Argument matched none of the accepted forms.           | Stop before Stage 1; show accepted formats from §3. |
| `ERR_WRONG_REPO`        | URL form pointed at a different owner/repo.             | Stop before Stage 1; do not silently use the issue number anyway. |
| `ERR_ISSUE_NOT_FOUND`    | Parsed `issueNumber` does not exist in the repo.        | Stop before Stage 1. |
| `ERR_STAGE_FAILED`        | A subagent reported `status: failed`.                   | Stop the pipeline at the failing stage; do not auto-retry past what that subagent's own rules allow. |

The orchestrator never silently skips a stage and never fabricates a
successful result for a stage that did not run.

## 7. Tool-permission model (overview)

This section documents the model that every subagent operates under. It is
the authoritative, repo-wide description of *how* tool access is scoped;
the authoritative list of *which specific tools* each subagent gets is
owned by that subagent's own file (Issues 2–5), not duplicated here, so the
two never drift out of sync.

Principles all subagents must follow:

1. **Least privilege per stage.** A subagent is granted only the tools its
   stage needs. For example, Architect/Planner is expected to be read-only
   (repo browsing, issue reading) with no write/commit access; Release/PR is
   the only stage expected to have git push / PR-creation access.
2. **No implicit escalation.** A subagent cannot grant itself a tool outside
   its declared list, and the orchestrator does not pass its own tool set
   down to subagents by default — each subagent's list is explicit and
   self-contained.
3. **Scoped file-system access.** Any subagent with write access is scoped
   to the paths relevant to its stage (e.g. the C# Implementer writes under
   `src/` and `tests/`; it does not need access to `.github/workflows/` or
   `docs/`).
4. **Auditability.** Every tool call a subagent makes as part of a pipeline
   run is attributable to that stage and that `issueNumber`, so a failed or
   unexpected change can be traced back to the exact stage that made it.
5. **Fail closed.** If a subagent's required tool is unavailable in the
   current environment, that stage reports `status: failed` with a clear
   reason rather than attempting a degraded workaround outside its scope.

The concrete tool lists for each stage are intentionally deferred to
Issues 2–5 so this file does not need to change every time a subagent's
tool needs evolve.

## 8. Out of scope for this issue

- The actual content of `agents/architect-planner.md`,
  `agents/csharp-implementer.md`, `agents/qa-self-healing.md`,
  `agents/release-pr.md` (Issues 2–5).
- Any files under `prompts/` and `rules/` beyond the placeholder
  (`.gitkeep`) needed to keep the empty directories under version control.
- End-to-end verification of the full routing/wiring between subagents —
  tracked as a follow-up check once Issues 2–5 land, before Issue 6.