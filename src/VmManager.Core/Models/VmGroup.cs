namespace VmManager.Core.Models;

public sealed record VmGroup(Guid Id, string Name, int SortOrder, IReadOnlyList<Guid> VmIds);
