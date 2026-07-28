using System.Runtime.CompilerServices;

namespace TNLAStation.Api.Tests;

internal static class DegradedStartupForTests
{
    [ModuleInitializer]
    internal static void Enable() =>
        Environment.SetEnvironmentVariable("AllowDegradedStartup", "true");
}
