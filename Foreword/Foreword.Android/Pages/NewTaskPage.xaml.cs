using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Tasks;

namespace Foreword.Android.Pages;

/// <summary>
/// T6. One field, and nothing else.
///
/// <para>Read it back is the only action, because accepting happens on the next screen: the owner
/// never saves something they have not seen parsed, so a misparse cannot quietly schedule the wrong
/// thing.</para>
///
/// <para>If Claude cannot be reached the sentence is <b>kept anyway</b>, as a plain once task titled
/// with their own words and marked unparsed, and it is read again on the next run that reaches
/// Claude. A rule typed on a dead network becomes a rule without anyone retyping it — losing it
/// because a network was down would be the app throwing away the only thing it was given.</para>
/// </summary>
public partial class NewTaskPage : BinderPage
{
    private readonly ITaskStore store = new TaskStore();

    public NewTaskPage()
    {
        InitializeComponent();
        Wrap(ForewordArea.Tasks, "New task");
    }

    private async void OnReadClicked(object sender, EventArgs e)
    {
        var sentence = SentenceEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return;
        }

        // A status sentence, not a spinner (DESIGN.md §6).
        StatusLabel.IsVisible = true;
        StatusLabel.Text = "Reading it…";
        ReadButton.IsEnabled = false;

        try
        {
            var reader = IPlatformApplication.Current!.Services.GetRequiredService<ITaskReadingClient>();

            if (await reader.Read(sentence) is { } read)
            {
                await Navigation.PushAsync(new TaskReadBackPage(sentence, read));
                return;
            }

            KeepUnread(sentence);
        }
        finally
        {
            ReadButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Nobody could read it. The sentence is kept as written and the owner is told plainly what will
    /// happen to it — an app that silently stored something different from what it showed them would
    /// be worse than one that failed.
    /// </summary>
    private void KeepUnread(string sentence)
    {
        store.Save(new ForewordTask
        {
            Id = Guid.NewGuid().ToString("n"),
            Title = sentence,
            Kind = TaskKind.Once,
            Unparsed = true,
            Written = sentence,
        });

        StatusLabel.Text =
            "Couldn't read it just now — kept as you wrote it, and it will print. "
            + "Foreword reads it again on the next morning it can.";
    }
}
