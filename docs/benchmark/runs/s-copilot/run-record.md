# Run record: s-copilot

Definitions of all metrics are in
[`methodology.md`](../../methodology.md).

## Setup

| Field | Value |
|-------|-------|
| Run id | `s-copilot` |
| Approach | B (interactive Copilot) |
| Issue | S (synthetic issue, overdue todos query), https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/issues/65 (#65) |
| Starting commit | `8278659e150346e070c48e1a99d10becf4ca1d0c` (merge base of the branch with `origin/main`) |
| Tool and model | VS Code 1.146.0 (commit `69047191fc42`, Windows x64), GitHub Copilot v0.74.0 (`@github/copilot` 1.0.108-0), model `copilot fusion - standard`, Agent mode |
| Operator | Alexandr Hemala |
| Deviations from the methodology | Commits were made manually by the operator after the run, not by Copilot. Their subjects were reworded afterwards (they first contained the placeholder `#<n>` instead of the issue number); the content was not changed. |

## M1 - Total Time to PR

| Field | Value |
|-------|-------|
| Started at | 2026-11-18 11:52 +01:00 |
| PR created at | 2026-11-18 12:15:07 +01:00 (from GitHub via `gh pr view`) |
| Total time to PR | 00:23:07 |
| Active human time (approach B) | not recorded |
| PR | https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4/pull/66 (#66) |

## M2 - Human Interventions

| # | Time | Category | What happened |
|---|------|----------|---------------|
| 1 | 11:53:21 | Approval | Approval of a tool call |
| 2 | 11:54:03 | Approval | Approval of a tool call |
| 3 | 11:54:42 | Approval | Approval of a tool call |
| 4 | 11:54:52 | Approval | Approval of a tool call |
| 5 | 11:55:18 | Approval | Approval of a tool call |
| 6 | 11:55:49 | Approval | Approval of a tool call |

Total: `6` (all approvals; no answers, corrections, manual edits or manual
commands were reported by the operator during the run).

## M3 - First-Attempt Pass Rate

| Field | Value |
|-------|-------|
| First verification passed | yes |
| Compile errors | 0 |
| Failed tests | 0 |

Source: operator report for the first verification. The script confirms the
final state: build exit code 0, test exit code 0, 338 tests total, 338 passed,
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

M5a (from `metrics.json`, collected on commit `14bc71b`):

| Field | Value |
|-------|-------|
| Files checked / clean | `10 / 9` |
| `FINALNEWLINE` / `ENDOFLINE` / `CHARSET` / `WHITESPACE` violations | `1 / 0 / 1 / 0` |
| `dotnet format --verify-no-changes` | pass |

Both violations are in `README.md` (no final newline, UTF-8 BOM where
`.editorconfig` expects none). They are present in the file on the base
branch as well, so they are not caused by the changes of this run; the script
checks whole files, not only the added lines. All changed `.cs` files and the
`.http` file are clean.

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

- The run produced 3 commits: `feat:` (production code), `test:` and `docs:`
  (README). Changed files: `TodosController.cs`, `ITodoRepository.cs`,
  `InMemoryTodoRepository.cs`, `Sharedjsonfiletodorepository.cs`,
  `UAI.ZeroVP.API.http`, three existing or new test files and `README.md`.
- No repair was needed: the first verification passed and the final state has
  338 of 338 tests green.
- All six interventions were approvals of tool calls, which an autonomous
  workflow with a pre-approved allow-list would not need.
- The commit subjects first contained the literal text `#<n>` instead of the
  issue number. This was an operator mistake, not a Copilot output; the
  subjects were reworded before the review.
- The six approvals were logged between 11:53:21 and 11:55:49, all within
  four minutes of the start. The active human time was not recorded.
