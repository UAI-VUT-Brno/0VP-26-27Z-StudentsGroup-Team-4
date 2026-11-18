# Technical Report: Autonomous Issue-to-PR Workflow vs Interactive Copilot

Issue: #56 (0VP-T4-20). Repository: `UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-4`.

> **Status of this document.** Sections 1-3 and the design-level part of
> section 5 describe the implemented workflow and are complete. Every cell or
> paragraph marked **PENDING RUN** needs data from the benchmark runs defined in
> [`methodology.md`](methodology.md) and must be filled in from the run records
> in `docs/benchmark/runs/`. No number in this report may be estimated.

## 1. Goal and scope

The team built `/issue-to-pr`, a skill that turns a GitHub issue into an
opened pull request without a human hand-off. This report evaluates that
autonomous workflow against GitHub Copilot used interactively in VS Code, on
two issues: the confirmed historical issue 0VP-T3-10 / 0VP-T3-13 (issue H) and
one newly assigned synthetic issue (issue S, [`synthetic-issue.md`](synthetic-issue.md)).

Five metrics are compared: Total Time to PR, Human Interventions, First-Attempt
Pass Rate, Self-Healing Efficiency, and C# Code Quality and Style (including
`.editorconfig` adherence). Definitions and measurement rules are in
[`methodology.md`](methodology.md) section 4. The standalone unattended-run
transcript is a separate artifact (see [`unattended-run.md`](unattended-run.md))
and is not part of the benchmark numbers.

## 2. Architectural design

### 2.1 Pipeline

`/issue-to-pr <ref>` is handled by an orchestrator (`skills/issue-to-pr/SKILL.md`)
that does only two things: parse and validate the issue reference, and hand
control to four subagents in a fixed order.

```text
/issue-to-pr <ref>
   |  parse + validate (number > 0, right repo, issue exists)
   v
Stage 1  Architect/Planner   read issue, parse plan, create branch
   v
Stage 2  C# Implementer      code bottom-up (DTO -> Data -> API -> tests), commit
   v
Stage 3  QA/Self-Healing     dotnet test, bounded repair loop, one commit per repair
   v
Stage 4  Release/PR          sync documentation, whitespace gate, push, open PR
```

Each stage returns a structured result with `status: ok | failed`. A `failed`
status stops the pipeline at that stage and surfaces the reason; the
orchestrator never skips a stage and never fabricates a successful result.

### 2.2 Stage responsibilities and tool scopes

| Stage | Agent | May write | Notable tools | Forbidden |
|-------|-------|-----------|---------------|-----------|
| 1 | Architect/Planner | git metadata only (one new branch) | `gh issue view`, `git status`, `git checkout -b` | any file edit, push, PR |
| 2 | C# Implementer | `src/`, `tests/` | file edit, `dotnet build`, `dotnet format --include <own files>`, `git add <explicit paths>`, `git commit` | running the test suite, branch changes, push, `gh` |
| 3 | QA/Self-Healing | `src/`, `tests/` | `dotnet test`, `dotnet format --include <own files>`, path-scoped `git commit` | branch changes, history rewriting, push, `gh`, edits outside `src/` and `tests/` |
| 4 | Release/PR | `docs/` and `README.md` only | `git diff --check origin/main...HEAD`, `git push --set-upstream origin <branch>`, `gh pr create` | any `dotnet` command, opening files under `src/` or `tests/` (code is seen only as diff output), force push, other `gh` commands |

### 2.3 Design principles

- **Least privilege per stage.** Every agent file has a strict allow-list table.
  Anything outside it ends the stage with `ERR_TOOL_OUT_OF_SCOPE` instead of
  a workaround ("fail closed"). The allow-list is prompt-level; the agent files
  also show an equivalent permission configuration for hard enforcement.
- **Single source of truth.** Branch names, commit format and PR format live
  only in `rules/repo-conventions.md`; layer rules and the definition of done
  live in `rules/architecture.md`. Agents reference these files and never
  define divergent variants. `skills/` is the source; `.claude/skills/` is a
  generated copy kept in sync by `scripts/sync-claude-skills.ps1`.
- **Lazy context loading.** Only `SKILL.md` is always loaded. Subagent prompts,
  rules and prompt fragments are read by the stage that needs them, which
  keeps each stage's context small and focused.
- **Layered quality gates.** Normalise (`dotnet format --include <own files>`),
  build, verify formatting (`--verify-no-changes`), run the CI test command
  (`--filter "Category!=Performance"`), then a whitespace gate over the whole
  branch (`git diff --check origin/main...HEAD`) before anything is pushed.
  The gates mirror the CI workflow, so a green run here predicts a green CI.
- **Auditable history.** The Implementer commits production code and tests
  separately; QA commits each repair on its own; Release commits documentation
  separately. Staging always uses explicit paths (never `git add .` or `commit -a`),
  and history is never rewritten, so a failed or surprising change can be traced
  to the stage and attempt that made it.

## 3. Prompt engineering and failure-handling techniques

| Technique | Where | Why it helps |
|-----------|-------|--------------|
| Locale-independent parsing: only exit codes, diagnostic ids, file paths and TRX fields are used; message text is never parsed | `prompts/failure-parsing.md` section 1 | .NET tools print in the system language (for example Czech), so keywords such as "Failed" are unreliable |
| Failure fingerprints `<id or test FQN> \| <file>` without line numbers | `failure-parsing.md` section 4 | Lets the agent tell "fixed", "new" and "unchanged after my fix" apart even when a fix shifts line numbers |
| Progress check between attempts: an unchanged fingerprint means the last fix did not work and must not be repeated | `qa-self-healing.md` step 3 | Prevents repeating the same failed repair until the budget is gone |
| Bounded repair loop: at most 4 QA repair attempts, at most 3 attempts per build or format failure in the Implementer | agent files | Guarantees termination and a complete failure report instead of an endless loop |
| One root cause per attempt, smallest possible change, fix compile errors before test failures in cascade order | `qa-self-healing.md` step 4, `failure-parsing.md` section 2.5 | Keeps repairs reviewable and avoids fixing symptoms of a cascade |
| Explicit list of forbidden shortcuts: skipping or deleting tests, weakening assertions, suppressing diagnostics, blanket `try/catch`, sleeps, project-file edits | `qa-self-healing.md` step 4 | Counters the most likely way an LLM "turns the build green" without fixing anything |
| Exception type to first hypothesis table | `failure-parsing.md` section 3.2 | Gives the model a prior for where the defect usually is (production code or test) |
| Early exit on out-of-scope failures (`NU####`, `MSB####`, environment errors such as a port in use) without consuming a repair attempt | `qa-self-healing.md` step 3 | Separates code defects from environment problems that no code change can fix |
| Zero discovered tests is a failure, not a pass (`ERR_NO_TESTS_RUN`) | `qa-self-healing.md` step 2 | Closes the "nothing ran, so nothing failed" loophole |
| Safe-start checks (right branch, clean index) before every stage | all agents, step 1 or 2 | Detects stale state from an earlier run; agents cannot unstage or switch branches, so they stop and report |
| Documentation-sync decision table with a skip path: only public contract, configuration or workflow changes trigger a doc edit, and only facts visible in the diff may be written | `release-pr.md` steps 2-3 | Prevents invented documentation and needless churn |
| Range-based whitespace gate `git diff --check origin/main...HEAD` | `release-pr.md` step 4 | A bare `git diff --check` only inspects uncommitted changes and would pass on a committed branch |
| Structured output contracts with stable error codes (`ERR_*`) and a final report that is always produced | all agents, section 5-6 | Makes failures machine-readable for the orchestrator and easy to analyse in this benchmark |

## 4. Benchmark results

All values come from `docs/benchmark/runs/<run-id>/run-record.md` and
`metrics.json`. One run per cell: indicative, not statistical.

**PENDING RUN.** Fill the table when the four runs are finished.

| Metric | H / Copilot | H / Autonomous | S / Copilot | S / Autonomous |
|--------|-------------|----------------|-------------|----------------|
| M1 Total Time to PR | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |
| M2 Human Interventions | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |
| M3 First-Attempt Pass (first verification green) | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |
| M4 Self-Healing Efficiency (`F0 / R`) | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |
| M5a `.editorconfig` violations (FINALNEWLINE / ENDOFLINE / CHARSET / WHITESPACE) and `dotnet format` | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |
| M5b Blind rubric score (max 10) | PENDING RUN | PENDING RUN | PENDING RUN | PENDING RUN |

Aggregated per approach (after the runs): first-attempt pass rate
`PENDING RUN` for Copilot and `PENDING RUN` for the autonomous workflow.

**Interpretation (PENDING RUN).** Discuss, with the numbers above: where the
autonomous workflow is faster or slower and why; what the interventions in the
Copilot runs were; whether self-healing converged and at what cost; whether
the explicit rule files measurably improved style adherence; and which
differences are likely noise given one run per cell.

## 5. Edge cases, failure modes and model limitations

### 5.1 Designed failure modes

These are handled by the specification (design analysis, not observed behaviour).

| Situation | Detection | Behaviour |
|-----------|-----------|-----------|
| Malformed reference, `0`, two numbers | Orchestrator input rules | `ERR_INVALID_REFERENCE`, nothing starts |
| URL of another repository | Orchestrator, owner/repo compare | `ERR_WRONG_REPO`, the number is not used silently |
| Issue missing / already closed | Orchestrator existence check | `ERR_ISSUE_NOT_FOUND` / explicit confirmation requested |
| Dirty working tree or existing branch | Stage 1 `git status --porcelain`, `git branch --list` | `ERR_DIRTY_WORKING_TREE` / `ERR_BRANCH_ALREADY_EXISTS`, no branch created |
| Issue without structured sections | Stage 1 parser | Goal taken from the whole body, warning `NO_STRUCTURED_SECTIONS_FOUND`, stage continues |
| Build or format check keeps failing | Stage 2, 3 attempts | `ERR_BUILD_FAILED` / `ERR_FORMAT_CHECK_FAILED`, nothing committed |
| Tests fail after 4 repairs | Stage 3 | Stops, lists every unresolved failure; nothing is pushed |
| Port in use, process start timeout (`Category=MultiProcess` tests) | Stage 3 failure parsing | `ERR_OUT_OF_SCOPE_FAILURE`, no attempt consumed |
| No tests discovered | Stage 3 | `ERR_NO_TESTS_RUN` |
| Whitespace errors outside `README.md` and `docs/` | Stage 4 whitespace gate | `ERR_DIFF_CHECK_FAILED`, nothing pushed |

### 5.2 Weak points found by analysing the design

These are risks identified from the specification and the repository state. They
should be confirmed or refuted by the runs.

- **Prompt-level enforcement.** The `tools:` lines and allow-list tables are
  instructions, not a sandbox. Without the client permission configuration a
  model can still call a forbidden command.
- **Green does not mean complete.** Performance tests are excluded from the
  loop (same filter as CI), so a green Stage 3 says nothing about them.
- **Fixing the test instead of the code.** The rules allow editing a defective
  test, which is exactly the shortcut the loop must not abuse. The
  forbidden-shortcuts list mitigates this but relies on the model's judgement.
- **Final-newline blind spot.** `git diff --check` does not report a missing
  final newline, so the Release stage's whitespace gate cannot catch it. Three
  files under `skills/issue-to-pr/` had this defect (cleaned before the
  benchmark). The Implementer's `dotnet format` normalisation covers `src/` and
  `tests/` only.
- **Prompts to a human inside an "autonomous" run.** A closed issue triggers a
  confirmation request; an unregistered skill or subagent (open items in
  `SKILL.md` section 8) or an untracked `.claude/` copy (treated as a dirty tree
  by Stage 1) stops the pipeline before any work happens.
- **Documentation language and style.** The Release agent keeps the language of
  the file it edits, while the team convention for new documents is English;
  mixed-language diffs are possible.
- **Stale copies.** `.claude/skills/` is generated; editing it by hand or
  forgetting to sync makes the run use outdated prompts.

### 5.3 Observed failures and model limitations

**PENDING RUN.** For every failure or surprise seen in the runs, add a row. Do
not generalise from a single occurrence.

| Run | Stage | Symptom | Root cause | Resolution / mitigation | Designed or new failure mode |
|-----|-------|---------|------------|-------------------------|------------------------------|
| PENDING RUN | | | | | |

## 6. Conclusions and follow-ups

**PENDING RUN.** State what the numbers support and what they do not, list the
prompt or rule changes the observations suggest, and name the open items that
remain (hard permission enforcement, committed `.claude/` copy, subagent
registration, CI check of the synchronised copy).

## Appendix: reproduction

```text
docs/benchmark/methodology.md       definitions and procedure
docs/benchmark/synthetic-issue.md   text of issue S
docs/benchmark/unattended-run.md    standalone transcript, pre-flight checklist
scripts/collect-run-metrics.ps1     objective metrics of a finished run
docs/benchmark/runs/<run-id>/       per-run records and metrics.json
```
