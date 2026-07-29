DESIGN.md - DeepDroidChanger Architecture & Folder Structure

This file defines mandatory rules for any AI agent or developer creating,
editing, or moving files in this project. If a change does not match a rule
here, stop and ask instead of guessing.

1. ARCHITECTURE

WPF Desktop app, .NET net10.0-windows, MVVM pattern.
DI via Microsoft.Extensions.Hosting (IHost), registered in App.xaml.cs.

Authentication is a separate `net10.0-windows` class library. Dependency
direction is strictly one-way:

`DeepDroidChanger -> DeepDroidChanger.Authentication`

The authentication project must never reference the WPF project, WPF
framework APIs, Views, ViewModels, device services, or WPF resources.
Consumers use only its public contracts and `AddDeepDroidAuthentication`.

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

  DeepDroidChanger.Authentication/ (Windows authentication class library)
    DeepDroidChanger.Authentication.csproj
    Constants/
      AccountStoreConstants.cs        internal account storage defaults
      AuthenticationConstants.cs      internal Cognito defaults
    DependencyInjection/
      AuthenticationServiceCollectionExtensions.cs
    Models/
      AccountAuthenticationResult.cs
      AccountLoginRequest.cs
      AccountSession.cs
      AccountStoreOptions.cs
      AuthenticationOptions.cs
      IdentityProviderAuthenticationResult.cs
    Services/
      Interfaces/
        IAccountAuthenticationService.cs
        IAccountStoreService.cs
        IAuthenticationSessionService.cs
        IIdentityProviderClient.cs
      Implementations/
        AccountAuthenticationService.cs
        AccountStoreService.cs
        AuthenticationSessionService.cs
        CognitoIdentityProviderClient.cs

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
      DeviceInfo/<Noun>.cs

    Services/
      Interfaces/
        I<Name>Service.cs        (shared service, no domain group)
        DialogServices/I<Name>DialogService.cs
        AdbServices/I<Name>Service.cs
        DeviceInfo/I<Name>Service.cs
      Implementations/
        <Name>Service.cs
        DialogServices/<Name>DialogService.cs
        AdbServices/<Name>Service.cs
        DeviceInfo/<Name>Service.cs

    Controls/     custom control / reusable UserControl
    Converters/   IValueConverter, IMultiValueConverter
    Behaviors/    attached behavior (Microsoft.Xaml.Behaviors)
    Helpers/      extension methods, static utilities
    Constants/    constants only, limited to:
      PropertyConstants.cs            Android/device property keys
      DeviceSettingsInfoConstants.cs  Android setting namespaces and keys
      UrlConstants.cs                 remote URLs
      AssetConstants.cs               asset/runtime paths and file names

    Resources/    XAML ResourceDictionary only
      Strings/
        Strings.xaml
        Strings.vi.xaml
        Views/<Feature>.xaml, <Feature>.vi.xaml
      Themes/
        Theme.Light.xaml  color tokens for light mode (Color.*, Brush.*)
        Theme.Dark.xaml   color tokens for dark mode (Color.*, Brush.*)
        DesignTokens.xaml shared Metric.*, Spacing.*, and Radius.* values
        Controls.xaml     aggregator for the control dictionaries below
        <ControlName>Control.xaml
                          reusable styles grouped by WPF control type
                          (ButtonControl.xaml, ComboBoxControl.xaml,
                          DataGridControl.xaml, etc.)
        ThemeManager.cs   runtime Light/Dark switch logic
        Generic.xaml      required if a Custom Control inherits Control

      See docs/THEMES.md for the full color palette, typography scale,
      spacing/radius tokens, and state rules. Never hardcode a color,
      font size, or corner radius in a View; always reference an
      existing Brush.*/Metric.*/Spacing.*/Radius.* key.

    Assets/       non-XAML files only
      Images/
      Icons/
      Fonts/
      Data/       carriers.json, location-timezones.json (embedded in app assembly)
      Tools/
        platform-tools/   adb.exe, fastboot.exe (Copy to Output)
        viewscreen/       scrcpy.exe and runtime dependencies (Copy to Output)

  DeepDroidChanger.Tests/
    DeepDroidChanger.Tests.csproj
    Authentication/ mirrors production Services/ of
                    DeepDroidChanger.Authentication
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

Constants/ in the WPF project holds constants only, no processing logic. It
must contain exactly the four catalog files listed in section 2. Only property
keys, Android setting namespaces/keys, URLs, and asset/runtime paths or file
names belong there. Authentication identifiers belong exclusively to the
authentication project. Operational values, command text, arguments, key-event
codes, timeouts, failure codes, option values, filters, column keys, and
localization resource keys must stay directly in the code that owns them.
Feature-local file extensions, temporary-directory names, manifest names, and
other workflow-specific file values are not shared assets; keep them directly
in the owning service.

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

Styles shared across the application belong in the matching
Resources/Themes/<ControlName>Control.xaml dictionary. Controls.xaml is only
the ordered aggregator, DesignTokens.xaml owns Metric.*/Spacing.*/Radius.*,
and Theme.Light.xaml / Theme.Dark.xaml own color tokens. A control dictionary
using a DesignTokens.xaml value through StaticResource must merge
DesignTokens.xaml directly so that the dictionary remains independently
loadable; aggregator order alone does not establish sibling StaticResource
scope.

Styles, templates, storyboards, geometries, and layout metrics used by only
one Window or UserControl belong directly in that view's
<Window.Resources> or <UserControl.Resources>. Do not create
Resources/Themes/<Feature>.xaml dictionaries or a separate
Views/<Feature>/Styles.xaml pattern.

Full color/typography/spacing rules: see docs/THEMES.md.

5. SERVICES

Every WPF-owned service needs an interface in Services/Interfaces/ and a
direct registration in App.xaml.cs.

Split services by domain when there are many:
DialogServices/  controls opening/closing dialogs, returns result to
                 the calling ViewModel
AdbServices/     interacts directly with the device via adb/scrcpy

Shared services with no domain (ISettingsService, IRandomService, etc)
stay flat directly in Services/Interfaces/ and
Services/Implementations/, no subfolder.

Authentication services are the exception to WPF service ownership: their
public interfaces live in `DeepDroidChanger.Authentication/Services/Interfaces`
and their implementations live in the matching `Services/Implementations`
folder. The authentication project exposes one DI composition method,
`AddDeepDroidAuthentication`, and keeps Cognito, DPAPI persistence, and atomic
file-writing details internal. Cognito configuration belongs to
`AuthenticationOptions`; account file persistence configuration belongs to
`AccountStoreOptions`. `AccountSession` contains only the ID token.

The Device Info GraphQL URL and authorization header are protected-resource
API configuration, not identity-provider configuration. They remain owned by
`DeviceInfoApiOptions` in the WPF project and use the names
`DeviceInfoGraphQlApi` and `AuthorizationHeaderName`. Device Info service
interfaces must not accept `AccountSession` or reference the authentication
namespace. `DeviceRandomApiService` is the integration boundary that reads
`IAuthenticationSessionService` and attaches the current ID token to the
resource request.

6. TESTS

DeepDroidChanger.Tests/ structure must mirror ViewModels/ and
Services/Implementations/ of the main project exactly. Authentication tests
live under `Authentication/` and mirror the new class library's production
Services/ structure.
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

Do not add a repository-wide `Settings/` ignore pattern:

- Runtime data is always created beside `DeepDroidChanger.exe`, using
  `AppContext.BaseDirectory` as the application directory. In a normal local
  build the executable is under the WPF project's
  `DeepDroidChanger/bin/<Configuration>/net10.0-windows/` output directory;
  after publish or deployment, the application directory is wherever the
  executable is located. Application settings and the remembered account live
  under `AppSettings/` (`app_settings.json` and `account.json`). The device
  index lives at `DeviceManager/devices.json`, and each serial has its own
  `DeviceManager/<serial>/` directory containing split per-device
  configuration JSON files. Random selection and random behavior settings
  share `random_config.json`; dialog-specific settings remain in their own
  files. Repository-local build outputs are already ignored because the project
  `bin/` directory is ignored.
- The Settings application feature is source code and resources, including
  `Views/Settings/SettingsView.xaml` and its local styles,
  `ViewModels/SettingsViewModel.cs`, Settings services and models, and
  `Resources/Strings/Views/Settings*.xaml`. These files must remain trackable.

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
  8.7. Put feature-specific styles in the new view's Resources. Add a style
     to the matching <ControlName>Control.xaml only when it is reusable by
     multiple views.
  8.8. Write tests for the ViewModel and Service in
     DeepDroidChanger.Tests/.
