# AGENTS.md

Entry point for every AI agent working in this repository.

## Required reading

Read only the documents relevant to the task:
- `docs/DESIGN.md` — before creating, editing, moving, renaming, or deleting
  project files.
- `docs/THEMES.md` — before changing XAML, styles, colors, typography, spacing,
  radii, themes, or interactive control states.
- `docs/GIT.md` — before any Git or version-control action.

Explicit safety, permission, build, dependency, and Git restrictions always
apply. If rules conflict, follow the user's current explicit instruction first,
then this file, then the relevant document under `docs/`.

## Research first

Before proposing or changing code:

1. Inspect the current repository, relevant implementation, neighboring
   patterns, public contracts, composition mechanism, resources, tests, and
   working-tree state.
2. Use concrete names and paths only after discovering them in the repository or
   receiving them from the user.
3. Treat documentation examples as illustrative unless explicitly marked as an
   invariant.
4. Extend an existing coherent pattern before creating a parallel abstraction.
5. Create only the files and layers required for the requested behavior.
6. Make the smallest safe change and preserve unrelated user work.

When documentation and repository reality differ, verify whether the repository
pattern is current and intentional. Preserve all explicit safety restrictions,
follow the current coherent implementation pattern, report the discrepancy, and
update documentation only when requested.

Do not stop because an example file or path is absent. Research the intended
boundary and continue safely. Ask only when unresolved ambiguity would
materially affect behavior, compatibility, security, data, dependencies,
architecture, or scope.
