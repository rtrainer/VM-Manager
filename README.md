# VM Manager

VM Manager is a Windows system-tray application for managing local Hyper-V virtual machines.

Current release: **0.2.2**

## MVP features

- Lists local Hyper-V VMs with state, CPU usage, and assigned memory.
- Refreshes VM status automatically every five seconds and on demand.
- Starts, gracefully shuts down, and force turns off individual VMs.
- Shows only valid actions for the selected VM state.
- Provides VM actions from the dashboard, dashboard right-click menu, and system tray.
- Creates custom groups and assigns one or more VMs to groups.
- Starts or shuts down every applicable VM in a group.
- Exposes VM group actions from the dashboard and system tray.
- Displays running VMs in bold green text in the system tray menu.
- Uses a centered confirmation dialog before forcefully turning off a VM.
- Hides the dashboard to the system tray when the window is closed.
- Reopens the dashboard by double-clicking the system tray icon.
- Shows startup progress while loading settings and discovering local Hyper-V VMs.
- Optionally starts minimized directly to the system tray.
- Provides a dedicated settings window from the dashboard gear button or tray menu.
- Displays the application release version in the settings window.
- Enforces a single running application instance.
- Uses a dedicated VM-stack icon throughout the application.

## Requirements

- Windows with Hyper-V enabled.
- A user account with permission to manage Hyper-V.
- The Windows Hyper-V PowerShell module.
- .NET 10 SDK to build from source.

## Run

```powershell
dotnet run --project .\src\VmManager.App\VmManager.App.csproj
```

Closing the dashboard hides it. Double-click the tray icon to reopen it, or use **Exit** from the tray menu.

## Local data

- Settings: `%LocalAppData%\VmManager\settings.json`
- VM groups: `%LocalAppData%\VmManager\groups.json`
- Startup and fatal-error diagnostics: `%LocalAppData%\VmManager\vm-manager.log`

## Build and test

```powershell
dotnet build .\VmManager.slnx
dotnet test .\VmManager.slnx
```

## Release version

Update the `<Version>` property in `src\VmManager.App\VmManager.App.csproj` for each release build. The value is embedded into the application metadata and displayed at the bottom of the settings window.

The MVP uses the installed Hyper-V PowerShell module behind `IHyperVService`. The adapter targets VMs by their stable Hyper-V ID, including machines with duplicate names.
