AGENTS.md - Entry point for AI agents

Read docs/DESIGN.md before creating, editing, or moving any file in this
project. It defines the mandatory architecture and folder structure rules:
MVVM layering, folder responsibilities (Views, ViewModels, Services, Models,
Resources, Assets, Converters, Behaviors, Constants), naming conventions,
service registration pattern, test structure, and the checklist for adding
a new feature.

Read docs/THEMES.md before writing or editing any XAML that involves
color, typography, spacing, corner radius, or control states (hover,
pressed, selected, focus, disabled, validation error). It defines the
full Light/Dark color palette (Brush.* tokens), typography scale,
spacing/radius tokens, and the mandatory state rules for every
interactive control.

Read docs/GIT.md before running any git command (commit, branch,
push, rebase, reset, move/rename files) or when the task involves
version control in any way. It defines commit message format
(Conventional Commits), branch naming, one-change-per-commit rule,
what must never be committed, and when destructive/remote actions
require explicit user request.

If a planned change does not match a rule in docs/DESIGN.md,
docs/THEMES.md, or docs/GIT.md, stop and ask instead of guessing.