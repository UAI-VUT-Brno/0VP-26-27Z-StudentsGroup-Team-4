# Unattended Run Transcript (Standalone Deliverable)

Issue: #56 (0VP-T4-20). This is deliverable 1 of the issue. It is **separate
from the benchmark**: one complete execution of `/issue-to-pr #<n>`, end to
end, without human intervention, ending in a merged or verified pull request.
It is archived as its own artifact and is not one of the four benchmark runs.

## 1. Pre-flight checklist

Do all of this **before** starting the recording. Anything that needs a human
during the run invalidates it.

| # | Check | How |
|---|-------|-----|
| 1 | The final-newline cleanup is merged | `tail -c1 <file> \| xxd` is `0a` for the three files listed in `methodology.md` section 3 |
| 2 | Working tree is clean | `git status --porcelain` prints **nothing**. Stage 1 treats untracked files as dirty (`ERR_DIRTY_WORKING_TREE`) |
| 3 | The skill and its subagents are discoverable by Claude Code | `bash scripts/sync-claude-skills.sh --check` (or `scripts/sync-claude-skills.ps1 -Check`) exits `0` |
| 4 | The generated `.claude/` copy does not make the tree dirty | The copy under `.claude/skills/` and `.claude/agents/` is committed; after the check of row 3, `git status --porcelain` is still empty |
| 5 | The subagents can be launched | Their `tools:` frontmatter lists Claude Code tool names (`Read, Bash`, ...); free-text entries make Claude Code refuse to spawn the agent with zero tools |
| 6 | Permissions are pre-approved | Start the run with `--settings .claude/issue-to-pr.settings.json`: the union of the per-stage allow-lists plus a deny list. In `-p` mode anything not allowed is denied, never prompted |
| 7 | GitHub access works non-interactively | `gh auth status` succeeds; the token can push branches and open PRs |
| 8 | The target issue is open and structured | Goal, requirements and acceptance criteria sections; not a closed issue (closed issues trigger a confirmation prompt) |
| 9 | The target branch name is free | `git branch --list feat/t3-<n>-*` is empty locally and on `origin` |
| 10 | Starting point is `main` and up to date | `git switch main && git pull --ff-only` |

## 2. Recording

Record **one** continuous run. Preferred: a non-interactive Claude Code
invocation whose full event stream is saved next to the PR evidence. Verify
the flag names with `claude --help` for the installed version.

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dir = "docs/benchmark/transcripts/unattended-run-$stamp"
New-Item -ItemType Directory -Path $dir | Out-Null
Get-Date -Format "o" | Set-Content "$dir/started-at.txt"
claude --settings .claude/issue-to-pr.settings.json -p "/issue-to-pr #<n>" `
    --output-format stream-json --verbose |
    Tee-Object -FilePath "$dir/transcript.jsonl"
Get-Date -Format "o" | Set-Content "$dir/finished-at.txt"
```

Alternatives, if the non-interactive mode cannot run the skill: record the
terminal session with `Start-Transcript`, or a screen recording of the whole
run (keep the clock visible). A recording must start before the command is
typed and end after the PR URL is shown.

## 3. What the archive must contain

`docs/benchmark/transcripts/unattended-run-<timestamp>/`:

| File | Content |
|------|---------|
| `transcript.jsonl` (or `.log` / `.mp4`) | The complete, unedited execution record |
| `README.md` | Filled from the template below |
| `started-at.txt`, `finished-at.txt` | Timestamps with time zone |

README template:

```markdown
# Unattended run <timestamp>

- Command: `/issue-to-pr #<n>`
- Issue: <link>
- Pull request: <link>, state: <merged | open, CI green>
- Commits on the PR branch: <count>, listed with `git log --oneline`
- Claude Code version / model: <...>
- Duration: <hh:mm:ss>
- Human interventions during the run: 0
- Redactions applied: <none | describe>

## Verification

- [ ] The transcript starts at the command and ends at the final report of the Release/PR stage
- [ ] No prompt was answered and no tool call was approved by hand
- [ ] No file was edited and no command was run outside the pipeline
- [ ] The PR exists, `Closes #<n>` is in its body, CI (`build-and-test`) is green
- [ ] The PR is merged, or a reviewer has verified it and noted the result here
```

## 4. After the run

- Search the transcript for secrets (tokens, keys) and redact them before
  committing; the repository is scanned with gitleaks (`.gitleaks.toml`).
- Commit the archive on its own, separate from the benchmark runs, for
  example `docs: archive unattended run transcript (#56)`.
- If the run **failed** or needed a human, keep the transcript, record the
  reason in the report (failure-mode analysis) and repeat the run from the
  checklist. Only a clean run satisfies the acceptance criterion.
