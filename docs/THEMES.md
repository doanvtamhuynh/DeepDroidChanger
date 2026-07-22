THEMES.md - DeepDroidChanger UI Theme Rules

This file defines the mandatory visual design system: colors,
typography, spacing, radius, and control states. It is the single
source of truth for Resources/Themes/. Never hardcode a color, font
size, or corner radius in a View; always reference an existing
Brush.*, Metric.*, Spacing.*, or Radius.* key defined here.

1. FILES

Resources/Themes/
  Theme.Light.xaml   Color.* and Brush.* tokens for light mode
  Theme.Dark.xaml    Color.* and Brush.* tokens for dark mode
  Controls.xaml      Metric.*, Spacing.*, Radius.* tokens, plus
                     default styles for Window, TextBlock, Button,
                     TextBox, ComboBox, CheckBox, RadioButton,
                     DataGrid, ListBox, TabControl, GroupBox, etc.
  ThemeManager.cs    AppTheme enum (Light, Dark) and
                     ThemeManager.Apply(theme) to swap Theme.Light/
                     Dark.xaml in Application.Resources.MergedDictionaries
                     at runtime.

Load order in App.xaml matters: color file first, MaterialDesign defaults,
Controls.xaml, localization strings, then feature-specific dictionaries.

  <ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Resources/Themes/Theme.Light.xaml"/>
    <ResourceDictionary Source="Resources/Themes/Controls.xaml"/>
    <ResourceDictionary Source="Resources/Strings/Strings.xaml"/>
    <ResourceDictionary Source="Resources/Themes/MainWindow.xaml"/>
  </ResourceDictionary.MergedDictionaries>

2. COLOR TOKENS (Brush.* - use DynamicResource, not StaticResource,
   so runtime theme switch works)

Surfaces
  Brush.WindowBackground   whole window background
  Brush.Surface            control/card background
  Brush.SurfaceAlt         secondary background, header, alt row
  Brush.SurfaceHover       mouse hover state
  Brush.SurfacePressed     mouse pressed state
  Brush.SurfaceSelected    selected item background

Text
  Brush.TextPrimary        main text
  Brush.TextSecondary      secondary/description text
  Brush.TextDisabled       disabled text

Borders
  Brush.Border             default border
  Brush.BorderStrong       stronger border (e.g. hover outline)
  Brush.Focus              keyboard focus outline (2px)

Actions
  Brush.Accent             primary action color
  Brush.AccentHover
  Brush.AccentPressed
  Brush.AccentForeground   text/icon color on top of Accent

Status
  Brush.Success
  Brush.Warning
  Brush.Danger
  Brush.DangerHover
  Brush.DangerPressed
  Brush.Overlay            modal backdrop overlay

Confirmation dialog
  Brush.BadgeInfoBackground question-mark/action badge surface
  Brush.BadgeInfoForeground question-mark/action badge glyph

Device viewer
  Brush.DeviceViewerStream stream canvas background
  Brush.DeviceViewerBody   device frame/body
  Brush.DeviceViewerScreen disconnected screen placeholder
  Brush.DeviceViewerGlyph  placeholder glyph

Light mode reference values: WindowBackground #F6F8FB, Surface
#FFFFFF, TextPrimary #172033, TextSecondary #526176, Border #CBD5E1,
Accent #2563EB, Success #15803D, Warning #B45309, Danger #DC2626,
BadgeInfoBackground #EFF6FF, BadgeInfoForeground #2563EB.

Dark mode reference values: WindowBackground #0F172A, Surface
#172033, TextPrimary #F8FAFC, TextSecondary #CBD5E1, Border #3B4A60,
Accent #3B82F6, Success #4ADE80, Warning #FBBF24, Danger #F87171,
BadgeInfoBackground #1B2A4A, BadgeInfoForeground #3B82F6.

Do not invent a new color outside this palette without updating both
Theme.Light.xaml and Theme.Dark.xaml together, so every new color has
a matching pair in both modes.

3. TYPOGRAPHY

Font family: Metric.FontFamily (Segoe UI Variable, Segoe UI), app-wide default.
Default weight: SemiBold, for readability (never Regular by default).

  Metric.FontSize.Micro      11   compact metadata
  Metric.FontSize.Small      12   captions, hints
  Metric.FontSize.Body       14   default text, inputs, buttons
  Metric.FontSize.Subtitle   16   section subtitle
  Metric.FontSize.SectionTitle 18 section heading
  Metric.FontSize.Title      20   page/section title, Bold weight
  Metric.FontSize.Display    24   dialog/page display title

Named text styles (Resources/Themes/Controls.xaml):
  TitleTextStyle       FontSize Title, Bold
  SubtitleTextStyle     FontSize Subtitle, Bold
  SecondaryTextStyle    Brush.TextSecondary, SemiBold

4. SPACING AND SHAPE

  Metric.ControlHeight       38   default input/button height
  Metric.ControlHeight.Comfortable 40 comfortable inputs
  Metric.DataGridRowHeight   40   standard data row
  Metric.DataGridEditorRowHeight 48 data row containing inline input controls
  Metric.ToolbarMinHeight    40   standard toolbar
  Metric.BorderThickness     1    default border width
  Metric.BorderThickness.Uniform 1 uniform Thickness resource
  Metric.Elevation.FloatingBlurRadius     28
  Metric.Elevation.FloatingShadowDepth    10
  Metric.Elevation.FloatingShadowOpacity  0.24
  Metric.Elevation.DialogBlurRadius       36
  Metric.Elevation.DialogShadowDepth      12
  Metric.Elevation.DialogShadowOpacity    0.28
  Metric.Animation.HoverDuration          0:0:0.17
  Metric.Animation.PressDuration          0:0:0.08
  Metric.Animation.EaseOut                CubicEase/EaseOut
  Metric.ConfirmationDialog.*             dialog-specific dimensions
  Spacing.ControlPadding     12,8
  Spacing.InputPadding       12,7
  Spacing.ItemPadding        12,9
  Spacing.PageMargin         16
  Spacing.CardPadding        16
  Spacing.CardPadding.Compact 12,10
  Spacing.ButtonPadding      16,8
  Spacing.ConfirmationDialog.* dialog-specific layout spacing

  Radius.Small    6    small controls, tags
  Radius.Medium   8    buttons, inputs, default
  Radius.Large    12   cards, panels, dialogs
  Radius.Circle   999  circular or pill-shaped elements
  Radius.Interactive 7 shared interactive surface
  Radius.Overlay  10 floating overlays
  Radius.TopMedium 8,8,0,0 top-rounded section header
  Radius.TopLeftMedium 8,0,0,0 top-left accent strip

5. CONTROL STATES (mandatory for every interactive control)

Every interactive control (Button, TextBox, ComboBox, CheckBox,
RadioButton, DataGrid row, ListBox item, etc) must define these
states using the Brush.* tokens above, not new colors:

  Normal      Brush.Surface / Brush.Border
  Hover       Brush.SurfaceHover
  Pressed     Brush.SurfacePressed
  Selected    Brush.SurfaceSelected
  Focus       2px Brush.Focus outline (use AppFocusVisualStyle)
  Disabled    reduced opacity, Brush.TextDisabled
  Validation error   Brush.Danger border + error text below the field

Named button styles: PrimaryButtonStyle (Brush.Accent), DangerButtonStyle
(Brush.Danger), default Button style for neutral actions.

6. DARK MODE RULE

Every screen must work in both Light and Dark without any code change
other than ThemeManager.Apply(theme). This means:
  - Never set a hardcoded Color/Brush in a View; always reference a
    Brush.* DynamicResource.
  - Test new screens in both themes before considering a feature done.

7. WHEN ADDING A NEW SCREEN

  7.1. Reuse existing named styles from Controls.xaml first
     (PrimaryButtonStyle, CardBorderStyle, TitleTextStyle, etc).
  7.2. If a truly new visual pattern is needed, add it to
     Resources/Themes/Controls.xaml (shared) or
     Resources/Themes/<Feature>.xaml (feature-specific only), never
     inline in the View.
  7.3. Any new color must be added to both Theme.Light.xaml and
     Theme.Dark.xaml with matching key names.
  7.4. Verify all 4 states (hover, pressed, selected/focus, disabled)
     are present for any new interactive control.
