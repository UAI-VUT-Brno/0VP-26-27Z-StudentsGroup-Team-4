# Run record: <run-id>

Copy this file to `docs/benchmark/runs/<run-id>/run-record.md` and fill it in
while the run happens. Definitions of all metrics are in
[`methodology.md`](methodology.md) (from the copied file under
`runs/<run-id>/` the relative path is `../../methodology.md`).

## Setup

| Field | Value |
|-------|-------|
| Run id | `h-autonomous` / `h-copilot` / `s-autonomous` / `s-copilot` |
| Approach | A (autonomous `/issue-to-pr`) / B (interactive Copilot) |
| Issue | H or S, GitHub link, replica link if used |
| Starting commit | `<sha>` on `<branch>` |
| Tool and model | Claude Code `<version>` + `<model>` / VS Code `<version>` + Copilot `<version>` + `<model>` |
| Operator | `<name>` |
| Deviations from the methodology | `<none | describe>` |

## M1 - Total Time to PR

| Field | Value |
|-------|-------|
| Started at | `<timestamp with time zone>` |
| PR created at | `<timestamp with time zone>` |
| Total time to PR | `<hh:mm:ss>` |
| Active human time (approach B) | `<hh:mm:ss>` |
| PR | `<link>` |

## M2 - Human Interventions

| # | Time | Category | What happened |
|---|------|----------|---------------|
| 1 | | Answer / Approval / Correction / Manual edit / Manual command | |

Total: `<n>`

## M3 - First-Attempt Pass Rate

| Field | Value |
|-------|-------|
| First verification passed | yes / no |
| Compile errors | `<n>` |
| Failed tests | `<n>` |

## M4 - Self-Healing Efficiency

| Field | Value |
|-------|-------|
| Distinct failures in the first verification (F0) | `<n>` |
| Repair cycles (R) | `<n>` |
| Efficiency (F0 / R) | `<value | n/a | not converged>` |
| Regression introduced by a repair | yes / no |
| Forbidden shortcut used | yes / no, describe |

| Cycle | Fingerprints before | Change made | Fingerprints after |
|-------|---------------------|-------------|--------------------|
| 1 | | | |

## M5 - C# Code Quality and Style

M5a (copy from `metrics.json`):

| Field | Value |
|-------|-------|
| Files checked / clean | `<n> / <n>` |
| `FINALNEWLINE` / `ENDOFLINE` / `CHARSET` / `WHITESPACE` violations | `<n> / <n> / <n> / <n>` |
| `dotnet format --verify-no-changes` | pass / fail |

M5b (blind rubric, 0-2 per criterion):

| Criterion | Reviewer 1 | Reviewer 2 | Agreed |
|-----------|:---:|:---:|:---:|
| Layers and repository contract | | | |
| API conventions and `ProblemDetails` | | | |
| Idiomatic C# | | | |
| Tests cover acceptance criteria | | | |
| Minimality and commit hygiene | | | |
| **Total (max 10)** | | | |

## Observations

Failure modes, surprises and model limitations seen during this run. Be
specific: stage, symptom, root cause, how it was resolved (or not).

-
