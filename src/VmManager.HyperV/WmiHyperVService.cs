using System.Globalization;
using System.Management;

using VmManager.Core.Models;
using VmManager.Core.Services;

namespace VmManager.HyperV;

public sealed class WmiHyperVService : IHyperVService {
    private const uint Completed = 0;
    private const uint Started = 4096;
    private const ushort EnabledState = 2;
    private const ushort DisabledState = 3;
    private const ushort CompletedJobState = 7;
    private readonly IHyperVWmiGateway _gateway;
    private readonly TimeSpan _jobPollInterval;

    public WmiHyperVService()
        : this(new SystemManagementHyperVWmiGateway(), TimeSpan.FromMilliseconds(250)) {
    }

    internal WmiHyperVService(IHyperVWmiGateway gateway, TimeSpan jobPollInterval) {
        _gateway = gateway;
        _jobPollInterval = jobPollInterval;
    }

    public Task<IReadOnlyList<VirtualMachine>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<VirtualMachine> virtualMachines = _gateway.GetVirtualMachineSummaries(cancellationToken)
                .Select(MapVirtualMachine)
                .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return virtualMachines;
        }, cancellationToken);

    public Task StartAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        Task.Run(() => {
            WmiMethodResult result = _gateway.RequestStateChange(vmId, EnabledState, cancellationToken);
            WaitForMethodResult(result, "start", cancellationToken);
        }, cancellationToken);

    public Task ShutDownAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        Task.Run(() => {
            WmiMethodResult result = _gateway.InitiateGuestShutdown(
                vmId,
                force: false,
                reason: "VM Manager requested a guest shutdown.",
                cancellationToken);
            WaitForMethodResult(result, "shut down", cancellationToken);
        }, cancellationToken);

    public Task TurnOffAsync(Guid vmId, CancellationToken cancellationToken = default) =>
        Task.Run(() => {
            WmiMethodResult result = _gateway.RequestStateChange(vmId, DisabledState, cancellationToken);
            WaitForMethodResult(result, "turn off", cancellationToken);
        }, cancellationToken);

    private void WaitForMethodResult(WmiMethodResult result, string action, CancellationToken cancellationToken) {
        if (result.ReturnValue == Completed) {
            return;
        }

        if (result.ReturnValue != Started) {
            throw new HyperVException($"Hyper-V could not {action} the virtual machine. Return value: {result.ReturnValue}.");
        }

        if (string.IsNullOrWhiteSpace(result.JobPath)) {
            throw new HyperVException($"Hyper-V started the {action} operation but did not return a job to monitor.");
        }

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            WmiJobStatus status = _gateway.GetJobStatus(result.JobPath, cancellationToken);
            if (status.State == CompletedJobState) {
                return;
            }

            if (IsFailedJobState(status.State)) {
                string description = status.ErrorDescription ?? $"Job state: {status.State}.";
                throw new HyperVException($"Hyper-V could not {action} the virtual machine. {description}");
            }

            if (cancellationToken.WaitHandle.WaitOne(_jobPollInterval)) {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static VirtualMachine MapVirtualMachine(WmiVirtualMachineSummary summary) =>
        new(
            summary.Id,
            summary.Name,
            MapEnabledState(summary.EnabledState),
            summary.ProcessorLoad,
            summary.MemoryUsageMb * 1024L * 1024L);

    private static bool IsFailedJobState(ushort jobState) =>
        jobState is 8 or 9 or 10 or 11;

    private static VirtualMachineState MapEnabledState(ushort enabledState) =>
        enabledState switch {
            0 => VirtualMachineState.Unknown,
            2 => VirtualMachineState.Running,
            3 => VirtualMachineState.Off,
            32768 => VirtualMachineState.Paused,
            32769 or 32773 => VirtualMachineState.Saved,
            32770 => VirtualMachineState.Starting,
            32774 => VirtualMachineState.Stopping,
            _ => VirtualMachineState.Other
        };
}

internal interface IHyperVWmiGateway {
    IReadOnlyList<WmiVirtualMachineSummary> GetVirtualMachineSummaries(CancellationToken cancellationToken);

    WmiMethodResult RequestStateChange(Guid vmId, ushort requestedState, CancellationToken cancellationToken);

    WmiMethodResult InitiateGuestShutdown(
        Guid vmId,
        bool force,
        string reason,
        CancellationToken cancellationToken);

    WmiJobStatus GetJobStatus(string jobPath, CancellationToken cancellationToken);
}

internal sealed record WmiVirtualMachineSummary(
    Guid Id,
    string Name,
    ushort EnabledState,
    int ProcessorLoad,
    long MemoryUsageMb);

internal sealed record WmiMethodResult(uint ReturnValue, string? JobPath);

internal sealed record WmiJobStatus(ushort State, string? ErrorDescription);

internal sealed class SystemManagementHyperVWmiGateway : IHyperVWmiGateway {
    private const string HyperVNamespace = @"\\.\root\virtualization\v2";
    private const uint Completed = 0;
    private static readonly uint[] SummaryInformationRequest = [
        0,   // Name
        1,   // ElementName
        100, // EnabledState
        101, // ProcessorLoad
        103  // MemoryUsage
    ];

    public IReadOnlyList<WmiVirtualMachineSummary> GetVirtualMachineSummaries(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ManagementScope scope = CreateConnectedScope();
        using ManagementObject managementService = GetVirtualSystemManagementService(scope);
        using ManagementBaseObject inParameters = managementService.GetMethodParameters("GetSummaryInformation");
        inParameters["RequestedInformation"] = SummaryInformationRequest;
        inParameters["SettingData"] = null;

        using ManagementBaseObject? outParameters = managementService.InvokeMethod("GetSummaryInformation", inParameters, null);
        if (outParameters is null) {
            throw new HyperVException("Hyper-V did not return virtual machine summary information.");
        }

        ThrowIfFailed(outParameters, "query virtual machines");
        if (GetPropertyValue(outParameters, "SummaryInformation") is not Array summaries) {
            return [];
        }

        var virtualMachines = new List<WmiVirtualMachineSummary>();
        foreach (ManagementBaseObject summary in summaries.OfType<ManagementBaseObject>()) {
            cancellationToken.ThrowIfCancellationRequested();
            using (summary) {
                WmiVirtualMachineSummary? virtualMachine = TryParseVirtualMachine(summary);
                if (virtualMachine is not null) {
                    virtualMachines.Add(virtualMachine);
                }
            }
        }

        return virtualMachines;
    }

    public WmiMethodResult RequestStateChange(Guid vmId, ushort requestedState, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ManagementScope scope = CreateConnectedScope();
        using ManagementObject virtualMachine = GetVirtualMachine(scope, vmId);
        using ManagementBaseObject inParameters = virtualMachine.GetMethodParameters("RequestStateChange");
        inParameters["RequestedState"] = requestedState;

        using ManagementBaseObject? outParameters = virtualMachine.InvokeMethod("RequestStateChange", inParameters, null);
        if (outParameters is null) {
            throw new HyperVException("Hyper-V did not return a result while requesting a virtual machine state change.");
        }

        return CreateMethodResult(outParameters);
    }

    public WmiMethodResult InitiateGuestShutdown(
        Guid vmId,
        bool force,
        string reason,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ManagementScope scope = CreateConnectedScope();
        using ManagementObject virtualMachine = GetVirtualMachine(scope, vmId);
        using ManagementObject shutdownComponent = GetShutdownComponent(virtualMachine);
        using ManagementBaseObject inParameters = shutdownComponent.GetMethodParameters("InitiateShutdown");
        inParameters["Force"] = force;
        inParameters["Reason"] = reason;

        using ManagementBaseObject? outParameters = shutdownComponent.InvokeMethod("InitiateShutdown", inParameters, null);
        if (outParameters is null) {
            throw new HyperVException("Hyper-V did not return a result while trying to shut down the virtual machine.");
        }

        return CreateMethodResult(outParameters);
    }

    public WmiJobStatus GetJobStatus(string jobPath, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ManagementScope scope = CreateConnectedScope();
        using var job = new ManagementObject(scope, new ManagementPath(jobPath), null);
        job.Get();

        return new WmiJobStatus(
            ToUInt16(GetPropertyValue(job, "JobState")),
            Convert.ToString(GetPropertyValue(job, "ErrorDescription"), CultureInfo.InvariantCulture));
    }

    private static ManagementScope CreateConnectedScope() {
        var scope = new ManagementScope(HyperVNamespace);
        scope.Connect();
        return scope;
    }

    private static ManagementObject GetVirtualSystemManagementService(ManagementScope scope) {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM Msvm_VirtualSystemManagementService"));
        using ManagementObjectCollection results = searcher.Get();
        foreach (ManagementObject result in results) {
            return result;
        }

        throw new HyperVException("The Hyper-V virtual system management service was not found.");
    }

    private static ManagementObject GetVirtualMachine(ManagementScope scope, Guid vmId) {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"));
        using ManagementObjectCollection results = searcher.Get();
        foreach (ManagementObject result in results) {
            if (Guid.TryParse(Convert.ToString(result["Name"], CultureInfo.InvariantCulture), out Guid candidateId)
                && candidateId == vmId) {
                return result;
            }

            result.Dispose();
        }

        throw new HyperVException($"Virtual machine '{vmId:D}' was not found.");
    }

    private static ManagementObject GetShutdownComponent(ManagementObject virtualMachine) {
        using ManagementObjectCollection components = virtualMachine.GetRelated("Msvm_ShutdownComponent");
        foreach (ManagementObject component in components) {
            return component;
        }

        throw new HyperVException("The virtual machine does not expose the Hyper-V shutdown integration component.");
    }

    private static WmiVirtualMachineSummary? TryParseVirtualMachine(ManagementBaseObject summary) {
        string? idValue = Convert.ToString(GetPropertyValue(summary, "Name"), CultureInfo.InvariantCulture);
        if (!Guid.TryParse(idValue, out Guid id)) {
            return null;
        }

        string name = Convert.ToString(GetPropertyValue(summary, "ElementName"), CultureInfo.InvariantCulture) ?? "(unnamed)";
        return new WmiVirtualMachineSummary(
            id,
            name,
            ToUInt16(GetPropertyValue(summary, "EnabledState")),
            ToInt32(GetPropertyValue(summary, "ProcessorLoad")),
            ToInt64(GetPropertyValue(summary, "MemoryUsage")));
    }

    private static WmiMethodResult CreateMethodResult(ManagementBaseObject outParameters) =>
        new(
            ToUInt32(GetPropertyValue(outParameters, "ReturnValue")),
            Convert.ToString(GetPropertyValue(outParameters, "Job"), CultureInfo.InvariantCulture));

    private static void ThrowIfFailed(ManagementBaseObject outParameters, string action) {
        uint returnValue = ToUInt32(GetPropertyValue(outParameters, "ReturnValue"));
        if (returnValue != Completed) {
            throw new HyperVException($"Hyper-V could not {action}. Return value: {returnValue}.");
        }
    }

    private static object? GetPropertyValue(ManagementBaseObject managementObject, string propertyName) {
        foreach (PropertyData property in managementObject.Properties) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                return property.Value;
            }
        }

        return null;
    }

    private static ushort ToUInt16(object? value) =>
        value is null ? (ushort)0 : Convert.ToUInt16(value, CultureInfo.InvariantCulture);

    private static uint ToUInt32(object? value) =>
        value is null ? 0U : Convert.ToUInt32(value, CultureInfo.InvariantCulture);

    private static int ToInt32(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static long ToInt64(object? value) =>
        value is null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
