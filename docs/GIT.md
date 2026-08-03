# Git Workflow Rules

## 1. Inspect and preserve

Before any write-capable Git action, inspect status, relevant staged and
unstaged diffs, untracked files, active branch, and repository conventions.
Preserve unrelated user work. Never overwrite, discard, stage, or include it in
the current task.

## 2. Permission boundaries

Do not commit, push, force-push, rebase, reset, clean, rewrite history, delete
branches/tags, change remotes, publish releases, or perform another destructive
or remote action unless the user explicitly requests it in the current task.
Implementation permission is not Git-history or remote permission.

When an authorized action may destroy work or affect collaborators, state the
exact action and risk before execution when the request is not sufficiently
specific.

## 3. Scope and changes

- Touch only files required by the task.
- Do not reformat, reorganize, revert, or resolve unrelated work.
- Preserve tracked-file history when moving content.
- Keep broad formatting and generated output separate from functional changes.
- Do not commit generated output unless the repository intentionally tracks it
  and the task requires it.

## 4. Commits and branches

Create commits or branches only when requested. Follow the repository's current
conventions after inspecting history and contributing documentation.

A commit should contain one coherent, independently reviewable change. When the
repository uses Conventional Commits, choose the type from the actual outcome,
for example `feat`, `fix`, `refactor`, `docs`, `test`, or `chore`. Do not invent
scopes or issue references.

Follow the existing branch naming convention. Do not switch branches when doing
so could hide, conflict with, or carry uncommitted work without protection.

## 5. Sensitive and local data

Never commit secrets, credentials, tokens, private keys, local environment
files, authentication captures, caches, IDE state, build output, temporary
extraction directories, local test artifacts, or runtime application data unless
that artifact is intentionally versioned and explicitly required.

Before changing ignore rules, resolve the full path, identify ownership and
behavior, inspect existing coverage, use the narrowest safe pattern, and verify
that legitimate source remains trackable.

## 6. Staging and conflicts

Stage only when it supports the requested workflow. Prefer explicit paths or
interactive review, and inspect the staged diff before a requested commit.

Do not resolve conflicts by choosing one side wholesale unless ownership is
unambiguous. Escalate conflicts involving product behavior, security, data, or
public semantics.

## 7. Reporting

Report commands executed, files intentionally changed, files left untouched,
staged/unstaged state when relevant, authorized commits/branches, remote actions
not performed, and remaining conflicts or risks.

Never claim changes were committed, pushed, merged, or clean unless verified.
