DESIGN.md - DeepDroidChanger Architecture & Folder Structure

This file defines mandatory rules for any AI agent or developer creating,
editing, or moving files in this project. If a change does not match a rule
here, stop and ask instead of guessing.

1. ARCHITECTURE

WPF Desktop app, .NET net10.0-windows, MVVM pattern.
DI via Microsoft.Extensions.Hosting (IHost), registered in App.xaml.cs.

Layering rule:
View (.xaml/.xaml.cs) - UI only: binding, pure UI events
ViewModel - exposes Property/Command, calls Service
Service - real business logic (adb commands, I/O, network)
Model - plain data object (POCO), no logic

Never call a Service directly from View code-behind. Always go through
ViewModel. Exception: pure Windows UI actions (OpenFileDialog,
SaveFileDialog) should still be wrapped in IDialogService so ViewModel
stays unit-testable.

2. FOLDER STRUCTURE

DeepDroidChanger/ (solution root)
  DeepDroidChanger.slnx
  global.json
  .gitignore
  AGENTS.md

  DeepDroidChanger/ (main WPF project)
    App.xaml, App.xaml.cs
    MainWindow.xaml, MainWindow.xaml.cs
    AssemblyInfo.cs
    DeepDroidChanger.csproj
    app.manifest

    Views/
      <Feature>/<Feature>View.xaml(.cs)
      Dialogs/<DialogName>/<DialogName>Dialog.xaml(.cs)

    ViewModels/
      <Feature>ViewModel.cs
      Dialogs/<DialogName>ViewModel.cs

    Models/
      <Noun>.cs   e.g. Device.cs, Carrier.cs, Timezone.cs
      AdbServices/<Noun>.cs
      Authentication/<Noun>.cs
      DeviceInfo/<Noun>.cs

    Services/
      Interfaces/
        I<Name>Service.cs        (shared service, no domain group)
        DialogServices/I<Name>DialogService.cs
        AdbServices/I<Name>Service.cs
        Authentication/I<Name>Service.cs
        DeviceInfo/I<Name>Service.cs
      Implementations/
        <Name>Service.cs
        DialogServices/<Name>DialogService.cs
        AdbServices/<Name>Service.cs
        Authentication/<Name>Service.cs
        DeviceInfo/<Name>Service.cs

    Controls/     custom control / reusable UserControl
    Converters/   IValueConverter, IMultiValueConverter
    Behaviors/    attached behavior (Microsoft.Xaml.Behaviors)
    Helpers/      extension methods, static utilities
    Constants/    constants only, no hardcoded string/number elsewhere

    Resources/    XAML ResourceDictionary only
      Strings/
        Strings.xaml
        Strings.vi.xaml
        Views/<Feature>.xaml, <Feature>.vi.xaml
      Themes/
        Theme.Light.xaml  color tokens for light mode (Color.*, Brush.*)
        Theme.Dark.xaml   color tokens for dark mode (Color.*, Brush.*)
        Controls.xaml     shared metrics + control styles (Metric.*,
                          Spacing.*, Radius.*, Button/TextBox/DataGrid
                          styles, states: hover/pressed/selected/focus/
                          disabled/validation error)
        ThemeManager.cs   runtime Light/Dark switch logic
        Generic.xaml      required if a Custom Control inherits Control
        <Feature>.xaml    only when a view has styles that are not
                          reusable elsewhere; keep flat, do not nest
                          under Views/<Feature>/

      See docs/THEMES.md for the full color palette, typography scale,
      spacing/radius tokens, and state rules. Never hardcode a color,
      font size, or corner radius in a View; always reference an
      existing Brush.*/Metric.*/Spacing.*/Radius.* key.

    Assets/       non-XAML files only
      Images/
      Icons/
      Fonts/
      Data/       carriers.json, timezones.json (Copy to Output)
      Tools/
        platform-tools/   adb.exe, scrcpy.exe (Copy to Output)

  DeepDroidChanger.Tests/
    DeepDroidChanger.Tests.csproj
    Architecture/ cross-cutting architecture, DI, security, resource,
                  and WPF surface smoke tests
    Fakes/        reusable test doubles only
    Helpers/      tests mirroring production Helpers/ and test-only
                  infrastructure shared by multiple fixtures
    ViewModels/   mirrors ViewModels/ of main project
    Services/     mirrors Services/Implementations/ of main project

3. FOLDER RULES

Resources/ holds only XAML ResourceDictionary: Strings/ and Themes/.
Never put images, json, or binaries there.

Assets/ holds only non-XAML files: images, icons, fonts, json data,
binary tools. Never put .xaml files there.

Converters/ holds only IValueConverter/IMultiValueConverter classes.
Extension methods go in Helpers/, not here.

Behaviors/ holds only attached behaviors bound from XAML. Business
logic goes in Services/.

Constants/ holds constants only, no processing logic.

Models/ holds plain POCO data only, no logic, no service calls, no I/O.

Services/Implementations/ holds all real logic: adb commands, file I/O,
HTTP calls. No UI code, no XAML binding here.

This naming matches the most common WPF convention. Do not reverse
Assets/Resources roles for any "runtime optimization" reason unless
this file is explicitly updated to say so.

4. STRINGS AND STYLES

Strings (localization): split per view, under
Resources/Strings/Views/<Feature>.xaml (+ .vi.xaml version).
Reason: easier to find text to translate, multiple translators can
work in parallel without conflicts.

Styles: keep shared, in Resources/Themes/Controls.xaml (metrics +
control styles) and Resources/Themes/Theme.Light.xaml /
Theme.Dark.xaml (color tokens). Do not create a Views/<Feature>/
Styles.xaml pattern for every view.
Only create a feature-specific style file
(Resources/Themes/<Feature>.xaml, flat, no nested folder) when a view
has styles that are truly not reusable elsewhere, e.g. a custom
progress-bar animation for one dialog.

Full color/typography/spacing rules: see docs/THEMES.md.

5. SERVICES

Every service needs an interface in Services/Interfaces/, registered
in App.xaml.cs.

Split services by domain when there are many:
DialogServices/  controls opening/closing dialogs, returns result to
                 the calling ViewModel
AdbServices/     interacts directly with the device via adb/scrcpy

Shared services with no domain (ISettingsService, IRandomService, etc)
stay flat directly in Services/Interfaces/ and
Services/Implementations/, no subfolder.

6. TESTS

DeepDroidChanger.Tests/ structure must mirror ViewModels/ and
Services/Implementations/ of the main project exactly.
Architecture/, Fakes/, and Helpers/ are the only permitted support
folders outside that mirror and must not contain production business
logic tests that belong under ViewModels/ or Services/.

Prioritize testing ViewModel (business flow) and
Services/Implementations (real logic). Views/ (pure UI) and Models/
(logic-free POCO) do not need tests.

7. .gitignore (required)

bin/
obj/
.vs/
*.user

Do not add a repository-wide `Settings/` ignore pattern. Two different
concepts use the word "Settings" in this project:

- Runtime data is created beside the executable under
  `bin/<Configuration>/net10.0-windows/Settings/` (for example,
  `settings.json`, `devices.json`, and `account.json`). It is already ignored
  because the entire `bin/` build output is ignored.
- The Settings application feature is source code and resources, including
  `Views/Settings/`, `ViewModels/SettingsViewModel.cs`, Settings services and
  models, and `Resources/Strings/Views/Settings*.xaml` /
  `Resources/Themes/Settings.xaml`. These files must remain trackable.

Before changing `.gitignore` for a path named `Settings`, first resolve its
actual location. Ignore the build-output path through `bin/`; never ignore a
source feature merely because its name is Settings.

8. CHECKLIST FOR ADDING A NEW FEATURE

  8.1. Create Models/<Name>.cs if new data is involved.
  8.2. Create Services/Interfaces/.../I<Name>Service.cs and
     Services/Implementations/.../<Name>Service.cs.
  8.3. Register in App.xaml.cs DI container.
  8.4. Create ViewModels/<Name>ViewModel.cs, inject service via
     constructor.
  8.5. Create Views/<Name>/<Name>View.xaml, set DataContext to the
     matching ViewModel.
  8.6. If there is display text: add to
     Resources/Strings/Views/<Name>.xaml (+ .vi.xaml version). Never
     hardcode strings in XAML or code-behind.
  8.7. If a feature-specific style is needed: add
     Resources/Themes/<Name>.xaml, merge into App.xaml.
  8.8. Write tests for the ViewModel and Service in
     DeepDroidChanger.Tests/.
