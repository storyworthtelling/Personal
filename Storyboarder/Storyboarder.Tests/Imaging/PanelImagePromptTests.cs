using Storyboarder.Domain.Imaging;
using Xunit;

namespace Storyboarder.Tests.Imaging;

public class PanelImagePromptTests
{
    private const string ActionClause =
        "Show the characters actively in this moment — caught mid-action and engaged with the scene, " +
        "not posing or facing the camera. ";

    [Fact]
    public void Build_WithoutReferences_OmitsTheConsistencyClause()
    {
        var prompt = PanelImagePrompt.Build("noir", "knight fights dragon", hasReferenceImages: false);

        Assert.Equal(
            "Comic panel in this style: noir. knight fights dragon. " +
            ActionClause +
            "Do not render any text, speech bubbles, or lettering in the image.",
            prompt);
    }

    [Fact]
    public void Build_WithReferences_IncludesTheAppearanceNotPoseClause()
    {
        var prompt = PanelImagePrompt.Build("noir", "knight fights dragon", hasReferenceImages: true);

        Assert.Equal(
            "Comic panel in this style: noir. knight fights dragon. " +
            ActionClause +
            "Match each character's and setting's appearance from the supplied reference images, but " +
            "not their pose — stage them in the action described, at a consistent relative scale. " +
            "Do not render any text, speech bubbles, or lettering in the image.",
            prompt);
    }

    [Fact]
    public void Build_AlwaysDirectsInActionFraming()
    {
        var prompt = PanelImagePrompt.Build("noir", "a quiet scene", hasReferenceImages: false);
        Assert.Contains("mid-action", prompt);
        Assert.Contains("not posing or facing the camera", prompt);
    }
}
