using System.Reflection;

namespace VmManager.App;

internal static class ApplicationVersion {
    public static string Display {
        get {
            Assembly assembly = typeof(App).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString(3)
                ?? "Unknown";
        }
    }
}
