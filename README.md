# VM Manager

VM Manager is a Windows system-tray application for managing local Hyper-V virtual machines.

Current release: **0.2.14-dev**

## MVP features

- Lists local Hyper-V VMs with state, CPU usage, and assigned memory.
- Monitors VM status automatically every five seconds and updates the dashboard and tray when state changes.
- Starts, gracefully shuts down, and force turns off one or more selected VMs concurrently.
- Allows additional VM power actions while other VM power actions are still running.
- Shows VMs as starting, shutting down, or turning off in the dashboard while power operations are running.
- Shows a system tray popup when VM startup, shutdown, or turn-off operations finish.
- Shows only valid actions for the selected VM state.
- Provides VM actions from the dashboard, dashboard right-click menu, and system tray.
- Creates custom groups and assigns one or more VMs to groups.
- Starts or shuts down every applicable VM in a group concurrently.
- Exposes VM group actions from the dashboard and system tray.
- Highlights running VMs with a pale green dashboard row background.
- Displays running VMs in bold green text in the system tray menu.
- Uses a centered confirmation dialog before forcefully turning off one or more VMs.
- Hides the dashboard to the system tray when the window is closed.
- Reopens the dashboard by double-clicking the system tray icon.
- Shows startup progress while loading settings and discovering local Hyper-V VMs.
- Starts automatically when the user signs in to Windows by default.
- Optionally starts minimized directly to the system tray.
- Provides a dedicated settings window from the dashboard gear button or tray menu.
- Displays the application release version in the settings window.
- Enforces a single running application instance.
- Uses a dedicated VM-stack icon throughout the application.

## Requirements

- Windows with Hyper-V enabled.
- A user account with permission to manage Hyper-V.
- The Windows Hyper-V WMI/CIM provider.
- .NET 10 SDK to build from source.

## Run

```powershell
dotnet run --project .\src\VmManager.App\VmManager.App.csproj
```

Closing the dashboard hides it. Double-click the tray icon to reopen it, or use **Exit** from the tray menu.

## Local data

- Settings: `%LocalAppData%\VmManager\settings.json`
- VM groups: `%LocalAppData%\VmManager\groups.json`
- Windows sign-in startup: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Startup and fatal-error diagnostics: `%LocalAppData%\VmManager\vm-manager.log`

## Build and test

```powershell
dotnet build .\VmManager.slnx
dotnet test .\VmManager.slnx
```

## Build installer

VM Manager uses Velopack for Windows installer and release packaging:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-installer.ps1
```

The script publishes a self-contained `win-x64` build and creates Velopack release assets in `artifacts\velopack`, including `LittleBitsSoftware.VmManager-win-Setup.exe`.

VM Manager checks for updates at startup, repeats the check every hour, and can also check manually from Settings or the tray menu. Automatic updates are off by default. When automatic updates are enabled, VM Manager shows a tray notification, downloads the update, and restarts to apply it. When automatic updates are disabled, VM Manager shows a tray notification and displays the available update in Settings with an update button. To make update checks available in a packaged build, host the Velopack release files from `artifacts\velopack` and pass that feed URL when building:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-installer.ps1 -UpdateUrl "https://example.com/vm-manager/releases"
```

## Tray icon cleanup

Windows can keep old notification-area registrations after running both a local debug build and the installed app. If duplicate VM Manager entries appear under **Settings > Personalization > Taskbar > Other system tray icons**, inspect them with:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\clear-stale-tray-icons.ps1
```

To remove stale VM Manager entries, run the same command with `-Apply`. Add `-RestartExplorer` if the Settings app does not refresh immediately.

## Release version

Update the `<Version>` property in `src\VmManager.App\VmManager.App.csproj` for each release build. The value is embedded into the application metadata and displayed at the bottom of the settings window.

When creating a feature branch, bump the version number and append `-dev`. When merging into `master`, remove the `-dev` suffix without bumping the numeric version again unless that is explicitly required.

## GitHub release workflow

The `Build Velopack Release` GitHub Action runs when `src\VmManager.App\VmManager.App.csproj` changes on `master`. If the `<Version>` value changed and a release tag for that version does not already exist, the workflow builds, tests, packages Velopack assets, and creates a GitHub release named `v<Version>`.

The workflow passes this update feed URL into the packaged app:

```text
https://github.com/<owner>/<repo>/releases/latest/download
```

You can also run the workflow manually from the GitHub Actions tab.

The MVP uses the Hyper-V WMI/CIM provider behind `IHyperVService`. The adapter targets VMs by their stable Hyper-V ID, including machines with duplicate names.
