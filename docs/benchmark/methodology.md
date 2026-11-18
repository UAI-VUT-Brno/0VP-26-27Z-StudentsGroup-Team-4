# Benchmark Methodology: Autonomous `/issue-to-pr` vs Interactive GitHub Copilot

Issue: #56 (0VP-T4-20). This document defines **how** the comparative
benchmark is run and measured, so that every number in
[`technical-report.md`](technical-report.md) can be reproduced and challenged.

The issue has two deliverables that must not be conflated:

| # | Deliverable | Where it lives | Depends on the benchmark? |
|---|-------------|----------------|---------------------------|
| 1 | Unattended run transcript of one full `/issue-to-pr #<n>` run | `docs/benchmark/transcripts/` (see [`unattended-run.md`](unattended-run.md)) | No - archived as its own artifact |
| 2 | Comparative benchmark and the 4-6 page technical report | `docs/benchmark/runs/` and `technical-report.md` | - |

## 1. Approaches under test

| Id | Approach | Description |
|----|----------|-------------|
| A | Autonomous | The `/issue-to-pr` skill (Claude Code): Architect/Planner -> C# Implementer -> QA/Self-Healing -> Release/PR, started with a single command and no human input. |
| B | Interactive Copilot | GitHub Copilot in VS Code (Agent mode), driven by a developer in the normal way: the developer pastes the issue, reviews and approves tool calls, answers questions, runs and fixes things when Copilot gets stuck. |

Record the exact tool and model versions of both approaches in every run
record (Claude Code version and model, VS Code version, Copilot extension
version and selected model). Models differ between the tools, so the
benchmark compares **complete workflows**, not the underlying models.

## 2. Issues under test

| Id | Issue | Source |
|----|-------|--------|
| H | The confirmed historical issue 0VP-T3-10 / 0VP-T3-13 | Closed GitHub issue; copy its exact text into `runs/<run-id>/issue.md` |
| S | The newly assigned synthetic issue | Draft in [`synthetic-issue.md`](synthetic-issue.md); open it on GitHub before the first run |

Both approaches get the **same issue text** and the **same starting commit**.

### 2.1 Starting commit

- **Issue S:** the tip of `main` after the prerequisite cleanup of section 3.
- **Issue H:** its original solution is already merged, so the work would be
  a no-op. Create a throwaway baseline branch that reverts the merged
  solution and use it as the starting commit for both approaches:

  ```bash
  git switch -c benchmark/baseline-h main
  git revert --no-edit <merge-commit-of-the-original-solution>
  ```

  If the revert conflicts because of later changes, resolve it minimally and
  record the resolution in the run record. Never run either approach on a
  branch that already contains the solution.

### 2.2 Open issue for the autonomous run

Stage 0 of the orchestrator asks for explicit confirmation when the issue is
already closed (`SKILL.md` section 4, step 8). That prompt would count as a
human intervention and break an unattended run. For approach A therefore open
a **replica issue** with the identical text of the historical issue, and run
`/issue-to-pr` against the replica. Approach B works from the same text.

## 3. Prerequisite baseline

Before any run, three files under `skills/issue-to-pr/` must end with a
final newline (`.editorconfig`: `insert_final_newline = true`):

- `agents/release-pr.md`
- `agents/architect-planner.md`
- `rules/repo-conventions.md`

`git diff --check` does not detect a missing final newline. Verify with:

```bash
tail -c1 <file> | xxd
```

The last byte must be `0a`. This is fixed in a single `docs:` commit so the
editorconfig-adherence metric starts from a clean baseline.

## 4. Metrics

All five metrics are recorded for **every** run (2 issues x 2 approaches).

### M1 - Total Time to PR

Wall-clock time from the moment the issue text is submitted to the tool until
the pull request exists on GitHub (`createdAt` reported by `gh pr view`).

- Record start and end timestamps with the time zone.
- For approach B also record **active human time** (time the developer was
  typing, reviewing or fixing, excluding waiting for the model) as a
  secondary value.
- Waiting for CI is not part of M1.

### M2 - Human Interventions

The number of actions by a human after the initial prompt that steer the
work. Log each one with a timestamp and a category in the run record:

| Category | Examples |
|----------|----------|
| Answer | Answering a clarifying question from the tool |
| Approval | Approving a tool call that the configured allow-list did not cover |
| Correction | Re-prompting because the output was wrong or incomplete |
| Manual edit | Editing a file by hand |
| Manual command | Running a build, test, git or gh command by hand that the tool should have run |

The first prompt itself is not an intervention. The target for approach A is
exactly 0.

### M3 - First-Attempt Pass Rate

Whether the first complete verification after the implementation succeeded
without any repair in between. The verification is exactly the CI command pair:

```bash
dotnet build UAI.ZeroVP.sln --configuration Release
dotnet test UAI.ZeroVP.sln --configuration Release --filter "Category!=Performance"
```

- A run **passes on the first attempt** when both commands exit with `0`, at
  least one test ran and no test failed.
- Per run the value is binary. Per approach the rate is
  `passed runs / total runs` (with two issues: 0/2, 1/2 or 2/2).
- For approach A take the baseline run of the QA stage (`attempts: 0`). For
  approach B the developer runs the same commands once the implementation
  looks complete and before fixing anything.
- Also record the number of failed tests and compile errors of that first
  verification as supporting detail.

### M4 - Self-Healing Efficiency

How effectively failures are repaired once the first verification failed.

- **Failures** `F0`: the number of distinct failure fingerprints in the first
  verification. A fingerprint is `<diagnostic id or test FQN> | <file>`, the
  same definition as `skills/issue-to-pr/prompts/failure-parsing.md` section 4.
- **Repair cycles** `R`: the number of edit -> re-verify rounds until both
  commands are green.
- **Efficiency** = `F0 / R` (failures resolved per repair cycle).

Special values: `n/a` when `F0 = 0` (nothing to heal), `not converged` when the
approach gave up or the budget was exhausted (approach A: 4 repair attempts),
and a note when a repair introduced a regression (more fingerprints than
before). Also record whether any repair used a forbidden shortcut (skipped,
deleted or weakened a test, suppressed a diagnostic).

### M5 - C# Code Quality and Style

Two parts, reported separately.

**M5a - objective, measured by the script.** Run
[`scripts/collect-run-metrics.ps1`](../../scripts/collect-run-metrics.ps1) on
the finished branch. For all files changed relative to `origin/main` it
reports violations of `.editorconfig` by rule:

| Rule id | Checks |
|---------|--------|
| `FINALNEWLINE` | file ends with a newline (`insert_final_newline`) |
| `ENDOFLINE` | line endings match the file type (CRLF by default; LF for `*.md`, `*.sh` and Docker/nginx files) |
| `CHARSET` | UTF-8 with BOM for `*.cs`, UTF-8 without BOM elsewhere |
| `WHITESPACE` | no trailing whitespace (`trim_trailing_whitespace`) |

plus the result of `dotnet format --verify-no-changes` on the changed files
under `src/` and `tests/` (which also covers analyzer severities such as the
security category). Editorconfig adherence is `clean files / checked files`.

**M5b - rubric review.** Two reviewers score the final diff independently
and **blind to the approach** (strip the branch name and the commit-message
style before reviewing). Each criterion gets 0 (violated), 1 (partly) or 2
(fully met), maximum 10:

| Criterion | Source |
|-----------|--------|
| Layer direction and repository contract respected (DTO -> Data -> API, all `ITodoRepository` implementations updated) | `rules/architecture.md` sections 2-4 |
| API conventions (routes, verbs, `ProblemDetails` errors, concurrency responses) | `rules/architecture.md` section 5 |
| Idiomatic C#: naming, nullability, async/cancellation, no dead code | general |
| Tests cover every testable acceptance criterion and are not weakened | `rules/architecture.md` section 6 |
| Minimality: nothing beyond the issue, commits are logical and follow `repo-conventions.md` | `rules/repo-conventions.md` |

Report both reviewer scores; if they differ by more than 2 points, discuss
and record the agreed value and the reason.

## 5. Fairness controls

- Same issue text, same starting commit, fresh session, no prior chat context.
- Approach B starts with only the issue text plus the default workspace
  context. It is **not** given the `skills/issue-to-pr/` rule files, because
  the point is to compare against interactive Copilot as it is normally used.
  If the team decides to give it those files, record that as a deviation.
- Approach A is configured as in [`unattended-run.md`](unattended-run.md),
  including the permission allow-list, so no approval prompts occur.
- Do not retry a run to "get a better result". If a run is repeated, keep all
  runs and report each. A single run per cell is an anecdote, not a
  statistical result: say so in the report.
- Run all four runs on the same machine and network, and note CI or API
  incidents that distorted M1.

## 6. Procedure per run

1. Create the starting branch (section 2.1) and make sure the working tree is
   clean (`git status --porcelain` prints nothing).
2. Create `docs/benchmark/runs/<run-id>/` with a copy of
   [`run-record-template.md`](run-record-template.md) as `run-record.md`.
   Run ids: `h-autonomous`, `h-copilot`, `s-autonomous`, `s-copilot`.
3. Note the start timestamp and submit the issue to the tool.
4. Work only as the approach allows. Log every human action (M2) while it
   happens, not afterwards.
5. When the PR exists, note the end timestamp and the PR number.
6. Collect objective metrics on the finished branch:

   ```powershell
   pwsh scripts/collect-run-metrics.ps1 -RunId <run-id> `
       -StartedAt "<start timestamp>" -PullRequest <pr-number>
   ```

7. Fill the rest of the run record (M3, M4 from the tool report or the
   developer log; M5b after the blind review) and commit it.
8. Copy the final numbers into the comparison table in
   `technical-report.md`.

## 7. Artifact layout

```text
docs/benchmark/
  methodology.md             this file
  synthetic-issue.md         text of issue S
  unattended-run.md          how to record the standalone transcript
  run-record-template.md     per-run record
  runs/<run-id>/
    run-record.md            filled template (M1-M5, intervention log)
    issue.md                 exact issue text given to the tool
    metrics.json             output of collect-run-metrics.ps1
  transcripts/               standalone unattended-run transcript (deliverable 1)
  technical-report.md        4-6 page report
```

## 8. Threats to validity

- Two issues and one run per cell: no statistical claims.
- Different tools use different models and different context handling.
- The developer operating approach B is also an author of the pipeline and
  may be faster or slower than an average user.
- M2 and M4 for approach B depend on honest, contemporaneous logging.
- The synthetic issue is designed by the team and may favour the rule files
  that the autonomous workflow reads.
