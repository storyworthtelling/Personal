using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.MealReadBack;

/// <summary>Two hooks behind the read-back's one: both are told, in order, and a failure in one never hides the other.</summary>
public class SettledHooksTests
{
    private sealed class Spy(string name, List<string> told, Exception? fails = null) : IReadBackSettled, IReadBackObserved
    {
        public void Settled(ExpectedDocument document, DocumentResult result)
        {
            told.Add(name + ":settled");
            if (fails is not null)
            {
                throw fails;
            }
        }

        public void Observed(ExpectedDocument document, DocumentResult result)
        {
            told.Add(name + ":observed");
            if (fails is not null)
            {
                throw fails;
            }
        }
    }

    private sealed class SettledOnly(List<string> told) : IReadBackSettled
    {
        public void Settled(ExpectedDocument document, DocumentResult result) => told.Add("plain:settled");
    }

    private static DocumentResult ResultOf(ExpectedDocument document) =>
        new(document.Name, document.Kind, document.Day, document.Person, Morning, []);

    [Fact]
    public void BothAreToldInTheOrderGiven()
    {
        var told = new List<string>();
        var hooks = new SettledHooks(new Spy("first", told), new Spy("second", told));
        var document = Workout();

        hooks.Settled(document, ResultOf(document));

        Assert.Equal(["first:settled", "second:settled"], told);
    }

    [Fact]
    public void AFailureInTheFirstStillTellsTheSecond()
    {
        var told = new List<string>();
        var failure = new InvalidOperationException("The first could not learn.");
        var hooks = new SettledHooks(new Spy("first", told, failure), new Spy("second", told));
        var document = Workout();

        var raised = Assert.Throws<InvalidOperationException>(() => hooks.Settled(document, ResultOf(document)));

        Assert.Same(failure, raised);
        Assert.Equal(["first:settled", "second:settled"], told);
    }

    [Fact]
    public void AFailureInTheSecondIsRaisedAsItIs()
    {
        var told = new List<string>();
        var failure = new IOException("The second could not write.");
        var hooks = new SettledHooks(new Spy("first", told), new Spy("second", told, failure));
        var document = Workout();

        var raised = Assert.Throws<IOException>(() => hooks.Settled(document, ResultOf(document)));

        Assert.Same(failure, raised);
        Assert.NotNull(raised.StackTrace);
    }

    [Fact]
    public void TwoFailuresAreBothKept()
    {
        var told = new List<string>();
        var first = new InvalidOperationException("one");
        var second = new IOException("two");
        var hooks = new SettledHooks(new Spy("first", told, first), new Spy("second", told, second));
        var document = Workout();

        var raised = Assert.Throws<AggregateException>(() => hooks.Settled(document, ResultOf(document)));

        Assert.Equal([first, second], raised.InnerExceptions);
    }

    [Fact]
    public void ObservingIsForwardedOnlyToThoseThatObserve()
    {
        var told = new List<string>();
        var hooks = new SettledHooks(new SettledOnly(told), new Spy("second", told));
        var document = Workout();

        hooks.Observed(document, ResultOf(document));

        Assert.Equal(["second:observed"], told);
    }

    [Fact]
    public void AFailureWhileObservingStillTellsTheRest()
    {
        var told = new List<string>();
        var failure = new InvalidOperationException("The first could not record.");
        var hooks = new SettledHooks(new Spy("first", told, failure), new Spy("second", told));
        var document = Workout();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => hooks.Observed(document, ResultOf(document))));
        Assert.Equal(["first:observed", "second:observed"], told);
    }

    [Fact]
    public void NoHookAtAllIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SettledHooks());
        Assert.Throws<ArgumentNullException>(() => new SettledHooks(new SettledOnly([]), null!));
    }
}
