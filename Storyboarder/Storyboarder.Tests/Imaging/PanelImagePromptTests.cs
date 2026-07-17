using Storyboarder.Domain.Imaging;
using Xunit;

namespace Storyboarder.Tests.Imaging;

public class PanelImagePromptTests
{
    [Fact]
    public void Build_WithoutReferences_OmitsTheConsistencyClause()
    {
        var prompt = PanelImagePrompt.Build("noir", "knight fights dragon", hasReferenceImages: false);

        Assert.Equal(
            "Comic panel in this style: noir. knight fights dragon. " +
            "Do not render any text, speech bubbles, or lettering in the image.",
            prompt);
    }

    [Fact]
    public void Build_WithReferences_IncludesTheConsistencyClause()
    {
        var prompt = PanelImagePrompt.Build("noir", "knight fights dragon", hasReferenceImages: true);

        Assert.Equal(
            "Comic panel in this style: noir. knight fights dragon. " +
            "Keep the characters and settings visually consistent with the supplied reference images. " +
            "Do not render any text, speech bubbles, or lettering in the image.",
            prompt);
    }
}
