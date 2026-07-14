GIT.md - DeepDroidChanger Git Workflow Rules

This file defines mandatory git rules for any AI agent or developer
working in this repository. If a task requires an action not covered
or conflicting with a rule here, stop and ask instead of guessing.

1. NO DESTRUCTIVE OR REMOTE ACTIONS WITHOUT EXPLICIT REQUEST

Do not commit, push, rebase, reset, or rewrite history unless the
user explicitly requests it in the current task. Preparing changes
and staging them is fine; finalizing history-changing actions is not,
unless asked.

2. COMMIT MESSAGE FORMAT (Conventional Commits)

Use one of these prefixes for every commit:
  feat:      new feature or capability
  fix:       bug fix
  refactor:  code change that is not a fix or a feature
  docs:      documentation only (README, docs/*.md, comments)
  test:      adding or updating tests only
  chore:     tooling, dependencies, config, build scripts, formatting

Example: feat: add DeviceManagerViewModel with refresh command

3. BRANCH NAMING

  feature/<short-description>
  fix/<short-description>
  refactor/<short-description>
  chore/<short-description>

Use lowercase, hyphen-separated short-description (e.g.
feature/device-manager-list).

4. ONE LOGICAL CHANGE PER COMMIT

Each commit must represent a single logical change. Do not bundle
unrelated changes (e.g. a new feature plus an unrelated bug fix) into
one commit.

5. DO NOT MIX FORMATTING WITH FUNCTIONAL CHANGES

If a change includes both broad formatting/reformatting and actual
logic changes, split them into separate commits: one chore:/refactor:
commit for formatting only, one feat:/fix: commit for the functional
change.

6. NEVER COMMIT THE FOLLOWING

  - Temporary extraction folders (e.g. unzip scratch folders, /tmp
    working directories)
  - User settings or runtime device data generated under
    `bin/<Configuration>/net10.0-windows/Settings/` (see docs/DESIGN.md
    section 7; `bin/` already excludes it from source control)
  - Secrets, API keys, tokens, connection strings, .env files
  - bin/, obj/, .vs/, *.user (already covered by .gitignore; verify
    before committing if unsure)

Do not interpret the runtime-data rule as permission to ignore every path
named `Settings`. The application's Settings feature (Views, ViewModels,
services, models, strings, and themes) is normal source and must remain
trackable. Never add a broad `Settings/` pattern to `.gitignore`; distinguish
paths by their full location as defined in docs/DESIGN.md section 7.

7. CHECK BEFORE EDITING

Run git status and git diff before starting any edit to detect
unrelated or uncommitted changes already present in the working tree.
Do not assume a clean working tree.

8. DO NOT OVERWRITE USER CHANGES OUTSIDE TASK SCOPE

Only touch files relevant to the current task. If git status shows
unrelated modified files, leave them untouched and mention them to
the user rather than reverting or overwriting them.

9. USE git mv FOR FILE/FOLDER MOVES

When renaming or moving a tracked file or folder, use git mv instead
of a plain filesystem move, so git history correctly tracks the
rename.
