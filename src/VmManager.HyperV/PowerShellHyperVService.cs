using System.Diagnostics;
using System.Text.Json;

using VmManager.Core.Models;
using VmManager.Core.Services;

namespace VmManager.HyperV;

public sealed class PowerShellHyperVService : IHyperVService {
    private const string GetVmsScript =
        "Get-VM | Sort-Object Name | Select-Object " +
        "@{n='Id';e={$_.VMId.ToString()}},Name,@{n='State';e={$_.State.ToString()}},CPUUsage,MemoryAssigned | " +
        "ConvertTo-Json -Compress";

    public async Task<IReadOnlyList<VirtualMachine>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default) {
        var output = await RunPowerShellAsync(GetVmsScript, cancellationToken);
        if (string.IsNullOrWhiteSpace(output)) {
            return [];
        }

        using var document = JsonDocument.Parse(output);
        List<JsonElement> elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];

        return elements.Select(ParseVirtualMachine).ToList();
    }

    public Task StartAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        RunPowerShellAsync(
            $"Get-VM -Id '{vmId:D}' -ErrorAction Stop | Start-VM -ErrorAction Stop | Out-Null",
            cancellationToken);

    public Task ShutDownAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        RunPowerShellAsync(
            $"Get-VM -Id '{vmId:D}' -ErrorAction Stop | Stop-VM -ErrorAction Stop | Out-Null",
            cancellationToken);

    public Task TurnOffAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        RunPowerShellAsync(
            $"Get-VM -Id '{vmId:D}' -ErrorAction Stop | Stop-VM -TurnOff -Force -ErrorAction Stop | Out-Null",
            cancellationToken);

    private static VirtualMachine ParseVirtualMachine(JsonElement element) {
        var id = Guid.Parse(element.GetProperty("Id").GetString()!);
        var name = element.GetProperty("Name").GetString() ?? "(unnamed)";
        VirtualMachineState state = ParseState(element.GetProperty("State").GetString());
        var cpuUsage = element.GetProperty("CPUUsage").GetInt32();
        var memoryAssigned = element.GetProperty("MemoryAssigned").GetInt64();
        return new VirtualMachine(id, name, state, cpuUsage, memoryAssigned);
    }

    private static VirtualMachineState ParseState(string? state) =>
        state switch {
            "Off" => VirtualMachineState.Off,
            "Running" => VirtualMachineState.Running,
            "Paused" => VirtualMachineState.Paused,
            "Saved" => VirtualMachineState.Saved,
            "Starting" => VirtualMachineState.Starting,
            "Stopping" => VirtualMachineState.Stopping,
            null or "" => VirtualMachineState.Unknown,
            _ => VirtualMachineState.Other
        };

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken) {
        var startInfo = new ProcessStartInfo {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"$ErrorActionPreference='Stop'; Import-Module Hyper-V -ErrorAction Stop; {script}");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try {
            await process.WaitForExitAsync(cancellationToken);
        } catch (OperationCanceledException) {
            process.Kill(true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) {
            throw new HyperVException(string.IsNullOrWhiteSpace(error)
                ? $"PowerShell exited with code {process.ExitCode}."
                : error.Trim());
        }

        return output.Trim();
    }
}
