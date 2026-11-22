# Run record: h-copilot

Definitions of all metrics are in
[`methodology.md`](../../methodology.md).

## Setup

| Field | Value |
|-------|-------|
| Run id | `h-copilot` |
| Approach | B (interactive Copilot) |
| Issue | H: 0VP-T4-10 "Add user identification to tasks without authentication", https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/issues/15 (#15), exact text in [`issue.md`](issue.md) |
| Starting commit | `eab5cac68639a4c3fe48520c7e8a1bbf91ae5b51` (branch `benchmark/baseline-h`, first parent of the merge of PR #24, i.e. the repository right before the original solution) |
| Run branch and PR | `benchmark/h-copilot`, PR #92 into `benchmark/baseline-h` (closed without merging) |
| Tool and model | VS Code 1.146.0, GitHub Copilot v0.74.0, model setting `copilot fusion - standard`, Agent mode. The model reported as used in this run was GPT-6. |
| Operator | Alexandr Hemala |
| Deviations from the methodology | See the list below. |

Deviations:

- The starting commit is not the tip of `main`: reverting the original
  solution on a recent commit conflicted in most files, so the baseline is the
  state right before PR #24 (methodology section 2.1, fallback).
- The prompt was the issue text only, without an introductory sentence, as in
  the `s-copilot` run.
- Commits were made manually by the operator after the run (three commits
  within two seconds); the PR was opened manually.
- `scripts/collect-run-metrics.ps1` does not exist at the starting commit. It
  was copied from the branch of the fix for #74 only to collect the metrics and
  removed afterwards.
- The baseline has no `.gitattributes`, so line endings and encoding of the
  files follow the operator's checkout settings (see M5a).

## M1 - Total Time to PR

| Field | Value |
|-------|-------|
| Started at | 2026-11-22 15:30 +01:00 |
| PR created at | 2026-11-22 15:55:47 +01:00 (14:55:47Z, from GitHub via `gh pr view`) |
| Total time to PR | 00:25:47 |
| Active human time (approach B) | not recorded |
| PR | https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/pull/92 (#92) |

## M2 - Human Interventions

| # | Time | Category | What happened |
|---|------|----------|---------------|
| 1 | 15:32 | Answer | Copilot asked whether `UserId` is required on creation; the operator answered "Yes" |
| 2 | 15:33 | Approval | Approval of a tool call (test) |
| 3 | 15:33 | Approval | Approval of a tool call (test) |
| 4 | 15:34 | Approval | Approval of a tool call (test) |

Total: `4` (1 answer, 3 approvals).

## M3 - First-Attempt Pass Rate

| Field | Value |
|-------|-------|
| First verification passed | yes (15:35) |
| Compile errors | 0 |
| Failed tests | 0 |

Source: operator notes for the first verification. The script confirms the
final state: build exit code 0, test exit code 0, 85 tests total, 85 passed,
0 failed.

## M4 - Self-Healing Efficiency

| Field | Value |
|-------|-------|
| Distinct failures in the first verification (F0) | 0 |
| Repair cycles (R) | 0 |
| Efficiency (F0 / R) | n/a (nothing to repair) |
| Regression introduced by a repair | no |
| Forbidden shortcut used | no |

| Cycle | Fingerprints before | Change made | Fingerprints after |
|-------|---------------------|-------------|--------------------|
| - | - | - | - |

## M5 - C# Code Quality and Style

M5a (from `metrics.json`, collected on commit `b73981a`, base
`benchmark/baseline-h`):

| Field | Value |
|-------|-------|
| Files checked / clean | `20 / 6` |
| `FINALNEWLINE` / `ENDOFLINE` / `CHARSET` / `WHITESPACE` violations | `13 / 177 / 13 / 0` |
| `dotnet format --verify-no-changes` | fail (exit code 2): `WHITESPACE` 36, `FINALNEWLINE` 13, `CHARSET` 13 |

How to read these numbers:

- All six files under `src/` that were changed are clean.
- The 12 test files and the performance test file each have a missing final
  newline and a missing BOM (13 files in total, as reported by both the script
  and `dotnet format`). The operator noted that `dotnet format
  --verify-no-changes` already failed on existing files, so these findings are
  probably inherited from the baseline, not added by the run. TODO: confirm by
  running `dotnet format --verify-no-changes` on `benchmark/baseline-h` and
  comparing the counts per rule.
- The 177 `ENDOFLINE` findings are all in `README.md`: the working-tree file
  has CRLF line endings while the checker expects LF for `*.md`. The baseline
  has no `.gitattributes`, so this is most likely an effect of the checkout
  settings, not of the change.
- The `WHITESPACE` count of the script (trailing whitespace) is 0; the 36
  `WHITESPACE` diagnostics of `dotnet format` are indentation findings that the
  script does not check.

M5b (blind rubric, 0-2 per criterion): not done yet.

| Criterion | Reviewer 1 | Reviewer 2 | Agreed |
|-----------|:---:|:---:|:---:|
| Layers and repository contract | | | |
| API conventions and `ProblemDetails` | | | |
| Idiomatic C# | | | |
| Tests cover acceptance criteria | | | |
| Minimality and commit hygiene | | | |
| **Total (max 10)** | | | |

## Observations

- Copilot asked one clarifying question at 15:32 and the operator answered that
  `UserId` is required on creation. The issue text does not say this, and the
  original solution (PR #24) made `userId` optional. The answer therefore
  steered the design; take this into account in the rubric.
- The issue text is only two sentences without goal, requirements or
  acceptance criteria. The run shows how Copilot handles an underspecified
  issue (compare with the autonomous workflow, which warns
  `NO_STRUCTURED_SECTIONS_FOUND`).
- The model setting `copilot fusion - standard` chooses the underlying model by
  itself: this run used GPT-6, while the operator remembers a Claude model in
  the `s-copilot` run (not verified). The two Copilot runs may not be
  comparable on the model; mention this in the threats to validity.
- No repair was needed: the first verification passed and the final state has
  85 of 85 tests green.
- The time between the passing verification (15:35) and the commits (15:50) was
  not logged in detail; it is part of M1. It includes reviewing the diff,
  running `dotnet format --verify-no-changes` and preparing the commits.
- The three commits are `feat:`, `test:` and `docs:` with subjects referring to
  `#15`.
