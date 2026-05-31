using System.Runtime.CompilerServices;
using DailyLoader.Infrastructure.Pdf;

namespace DailyLoader.Tests;

// The PDF composers are font-agnostic; a host registers the font resolver. For tests there is
// no host, so register the system resolver once before any test runs (mirrors the Worker and
// the MAUI app registering their resolvers at startup).
internal static class TestFontSetup
{
    [ModuleInitializer]
    public static void Init() => SystemFontResolver.Register();
}
