namespace Storyworthtelling.Maui;

/// <summary>
/// The Paper &amp; Ink dictionary, as a type a head can merge.
///
/// <para>It carries an <c>x:Class</c> for one reason: a XAML <c>Source=</c> resolves only inside its
/// own assembly, so a head cannot merge a library's dictionary by path. It adds
/// <c>new PaperKit()</c> to its merged dictionaries instead, which is also why this file exists at
/// all — an empty code-behind that would otherwise look like leftover scaffolding.</para>
/// </summary>
public partial class PaperKit : ResourceDictionary
{
    public PaperKit()
    {
        InitializeComponent();
    }
}
