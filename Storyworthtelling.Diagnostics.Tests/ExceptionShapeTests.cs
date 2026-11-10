using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// A failure from a place that may hold values (Body readings, a meal, a balance) is logged by its shape: the type
/// chain and the frames, which carry no values, and never a message (audit 08 §9).
/// </summary>
public class ExceptionShapeTests
{
    [Fact]
    public void TheShapeIsTypesAndFramesWithoutTheMessage()
    {
        Exception caught;
        try
        {
            Throw();
            return;
        }
        catch (Exception e)
        {
            caught = e;
        }

        var shape = ExceptionShape.Describe(caught);

        var lines = shape.Split('\n');
        Assert.Equal("System.InvalidOperationException < System.FormatException", lines[0]);
        Assert.Contains(lines, line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal) && line.Contains(nameof(Throw)));
        Assert.DoesNotContain("72.4", shape);
    }

    [Fact]
    public void AnExceptionNeverThrownIsJustItsTypes() =>
        Assert.Equal("System.TimeoutException", ExceptionShape.Describe(new TimeoutException("weight 72.4")));

    private static void Throw()
    {
        try
        {
            throw new FormatException("weight 72.4 kg");
        }
        catch (FormatException inner)
        {
            throw new InvalidOperationException("reading 72.4 kg failed", inner);
        }
    }
}
