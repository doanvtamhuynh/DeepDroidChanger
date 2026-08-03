# UI Theme and Visual-System Rules

## 1. Research and source of truth

Before editing XAML or visual resources, locate the active themes, semantic
tokens, shared styles, local resources, merge/load order, and runtime theme
switching mechanism. The current repository is the source of truth for concrete
resource names and values.

Do not create new tokens, dictionaries, styles, or theme managers until existing
resources and ownership have been inspected.

## 2. Semantic visual rules

Do not hardcode theme-sensitive colors, brushes, typography, spacing, radii,
borders, shadows, dimensions, or animation values when an existing semantic
token or style represents the role.

Every theme-sensitive role must resolve in every supported theme. Preserve
contrast, hierarchy, runtime theme switching, and dynamic resource behavior.
Keep resources at the narrowest appropriate scope:

- one-view use: local resource;
- proven multi-view use: shared style/resource;
- application-wide meaning: shared semantic token.

Do not promote a local style based on speculative reuse or rely on accidental
resource load order.

## 3. Typography, layout, and states

Use the existing font family, type scale, spacing, sizing, radius, border,
elevation, and motion systems. Add a value only when no existing semantic role
fits and ownership is clear.

Interactive controls must expose relevant states:

- normal;
- hover;
- pressed/active;
- selected/checked;
- keyboard focus;
- disabled;
- validation error when applicable.

Reuse existing state tokens and named styles. Preserve keyboard navigation,
visible focus, readable disabled content, validation feedback, and distinctions
that do not rely only on color when practical.

Visual changes must tolerate localization, text growth, supported window sizes,
and high-DPI scaling. Avoid fixed dimensions that unnecessarily truncate
content.

## 4. Screen changes

For a new or redesigned screen:

1. identify information hierarchy and primary actions;
2. reuse existing semantic tokens and styles;
3. keep feature-specific resources local;
4. promote only proven reusable patterns;
5. cover relevant interaction and validation states;
6. inspect all supported themes;
7. consider keyboard use, localization, resizing, and DPI;
8. avoid unrelated global-style changes.

Do not pre-create resource dictionaries or keys from a fixed template.

## 5. Validation

Validate or inspect, as applicable:

- supported themes and theme switching;
- normal and interactive states;
- keyboard focus and accessibility;
- disabled and validation states;
- contrast and typography hierarchy;
- localization expansion, resizing, and DPI;
- resource lookup and unrelated global regressions.

UI validation may be performed through safe inspection, manual checks,
screenshots, or existing UI-test tooling. Do not add UI tests to
`DeepDroidChanger.Test`; that project is reserved for non-UI function and logic
tests. New persistent UI-test code requires an established separate location or
explicit user approval.