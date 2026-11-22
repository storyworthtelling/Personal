using Codex.Domain.Diagnostics;
using Xunit;

namespace Codex.Tests.Diagnostics;

// A policy call that throws a bare Java RuntimeException used to log only the .NET wrapper's type and
// "(no message)", which says nothing. The line now names the Java class and where it was thrown from.
public class JavaThrowableTextTests
{
    private static readonly string[] Frames =
    [
        "android.example.ExampleManager.setThing",
        "android.example.ExampleProxy.call",
        "android.example.Binder.transact",
        "android.example.Looper.loop",
    ];

    [Fact]
    public void NamesTheJavaClassAndItsTopThreeFrames()
    {
        var text = JavaThrowableText.Describe([new JavaThrowableLink("java.lang.RuntimeException", null, Frames)]);

        Assert.Equal(
            "java.lang.RuntimeException: (no message) at android.example.ExampleManager.setThing, android.example.ExampleProxy.call, android.example.Binder.transact",
            text);
    }

    [Fact]
    public void KeepsTheMessageAndWalksTwoCausesDeep()
    {
        var text = JavaThrowableText.Describe(
        [
            new JavaThrowableLink("java.lang.RuntimeException", null, ["a.example.One.run"]),
            new JavaThrowableLink("android.os.ServiceSpecificException", "example failure", ["a.example.Two.run"]),
            new JavaThrowableLink("java.lang.SecurityException", "example refusal", []),
            new JavaThrowableLink("java.lang.IllegalStateException", "too deep", []),
        ]);

        Assert.Equal(
            "java.lang.RuntimeException: (no message) at a.example.One.run"
            + " <- android.os.ServiceSpecificException: example failure at a.example.Two.run"
            + " <- java.lang.SecurityException: example refusal",
            text);
    }

    [Fact]
    public void AnEmptyChainSaysSo()
    {
        Assert.Equal("(no exception)", JavaThrowableText.Describe([]));
    }
}
