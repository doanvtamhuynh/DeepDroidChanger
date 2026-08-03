# Architecture and Repository Design Rules

## 1. Interpretation and research

This document defines architectural boundaries, not a file-generation template.
Examples are illustrative unless explicitly marked as invariants.

Apply decisions in this order:

1. the user's current explicit instruction;
2. repository agent instructions and safety rules;
3. invariants in this document;
4. current coherent repository patterns;
5. framework and language conventions;
6. a new design only when the earlier sources do not resolve the decision.

Before creating, moving, renaming, deleting, or substantially editing files,
inspect the relevant tree, neighboring implementations, public contracts,
composition root, resources, tests, extension points, and working-tree state.
Use concrete names and paths only after discovering them. Prefer extending an
existing pattern over introducing a parallel abstraction.

## 2. Architectural boundaries

Preserve MVVM and responsibility separation:

- **View:** rendering, binding, visual composition, and UI-only events.
- **ViewModel:** presentation state, commands, validation flow, and service
  orchestration.
- **Service/Application:** workflows and external operations.
- **Model/Contract:** data representation without hidden I/O or UI behavior.
- **Infrastructure:** concrete platform, persistence, protocol, and system
  integrations.

Views must not perform business operations directly. Put file pickers, dialogs,
platform APIs, process execution, device commands, networking, persistence, and
other side effects behind the repository's existing testable boundaries.

Use the current dependency-injection and composition mechanism. Do not create a
second registration system or reverse established dependency directions.
Security-sensitive components must remain isolated from presentation and
provider-specific implementation details.

## 3. Organization and ownership

Organize code by responsibility and current repository convention. Create a new
folder, interface, service, resource dictionary, helper, or abstraction only
when it has clear ownership and architectural value.

Do not automatically create a model, interface, implementation, ViewModel,
View, resource dictionary, and test file for every feature. Create only what the
requested behavior requires.

Keep ownership clear:

- UI resources remain separate from images, fonts, data files, tools, runtime
  data, and generated output.
- Feature-local resources stay local until proven reusable.
- Shared semantic resources belong in the existing shared resource system.
- Stable shared identifiers may use existing constants; environment-dependent
  values belong in configuration; workflow-local values stay local.
- User-visible text follows the existing localization system.
- Runtime data, secrets, caches, and generated output remain outside tracked
  source locations.

## 4. Testing architecture

Use `DeepDroidChanger.Test` only for focused non-UI tests of functions and
observable application logic. Suitable targets include parsing, formatting,
validation, mapping, calculations, ViewModel logic without real rendering, and
service/workflow behavior through mocks or fakes.

Do not write UI automation or rendering tests in `DeepDroidChanger.Test`. Tests
that require real Views, Windows, dialogs, controls, visual-tree interaction,
keyboard/pointer automation, screenshots, or theme appearance belong outside
that project and require an established UI-test location or explicit user
approval to create one.

Agents may still validate the UI through safe inspection, manual checks,
screenshots, accessibility/state review, or existing UI-test infrastructure.
Report UI validation separately from function tests.

## 5. Change principles

For every change:

- make the smallest defensible modification;
- preserve public behavior unless change is requested;
- reuse current extension points and conventions;
- avoid speculative abstractions and premature generalization;
- avoid unrelated formatting, moves, renames, and rewrites;
- keep local decisions local until reuse justifies promotion;
- document assumptions and unresolved risks;
- report unrelated issues instead of fixing them silently.

When adding or changing a feature, determine from repository evidence whether it
needs a contract, service boundary, registration, presentation state, View,
localized text, resources, tests, configuration, migration, or compatibility
handling. Do not generate all categories automatically.

## 6. Stop conditions

Stop and request guidance when the change would conflict with an invariant,
reverse a dependency, break a public contract, risk security or data loss,
require a destructive migration, or establish a new system-wide convention.
Also stop when conflicting repository patterns cannot be resolved through
inspection.

Do not stop merely because an example path or artifact is absent. Research the
current repository and preserve the rule's intent.
