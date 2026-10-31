using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// D99, structurally: no picture ever reaches Constitution, so no scan type has anywhere to hold one. This walks every member of
/// the scan models, the keeper and the judge seam (fields, properties, constructors, method returns and parameters, generic
/// arguments) and fails on an image, a stream, a byte array, a URI or a camera type, as the Scraps and Birthdate guards do for theirs.
/// </summary>
public class ScanShapeTests
{
    private const BindingFlags Everything = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Regex Pictorial = new(
        "Image|Bitmap|Photo|Picture|Jpeg|Jpg|Png|Camera|Pixel|Frame",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));

    private static readonly string[] Seams =
    [
        nameof(IRecipeKeeper), nameof(RecipeKeepResult), nameof(NoRecipeKeeper),
        nameof(UncertainWord), nameof(IRecipeWordJudge), nameof(NoRecipeWordJudge), nameof(RecipeReadPayload),
    ];

    private static IReadOnlyList<Type> ScanTypes() =>
        typeof(ScanConfirmModel).Assembly.GetTypes()
            .Where(type => type.Namespace is "Constitution.Domain.Recipes.Screens" or "Constitution.Domain.Recipes")
            .Where(type => (type.DeclaringType ?? type).Name.StartsWith("Scan", StringComparison.Ordinal) || Seams.Contains((type.DeclaringType ?? type).Name))
            .ToList();

    /// <summary>What is wrong with this type's members, one line each; empty when it cannot hold a picture.</summary>
    internal static IReadOnlyList<string> Offences(Type type)
    {
        var found = new List<string>();
        foreach (var member in type.GetMembers(Everything))
        {
            if (Pictorial.IsMatch(member.Name))
            {
                found.Add(type.Name + "." + member.Name + " is named like a picture.");
            }

            foreach (var held in TypesOf(member))
            {
                if (Forbidden(held))
                {
                    found.Add(type.Name + "." + member.Name + " can hold " + held.Name + ".");
                }
            }
        }

        return found;
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field:
                return Expand(field.FieldType);
            case PropertyInfo property:
                return Expand(property.PropertyType);
            case MethodBase method:
                return method.GetParameters().SelectMany(parameter => Expand(parameter.ParameterType))
                    .Concat(method is MethodInfo info ? Expand(info.ReturnType) : []);
            default:
                return [];
        }
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var inner in Expand(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Expand))
            {
                yield return argument;
            }
        }
    }

    private static bool Forbidden(Type type) =>
        type == typeof(Uri)
        || typeof(Stream).IsAssignableFrom(type)
        || type == typeof(byte[])
        || type == typeof(Memory<byte>) || type == typeof(ReadOnlyMemory<byte>)
        || type == typeof(Span<byte>) || type == typeof(ReadOnlySpan<byte>)
        || (type.Namespace ?? string.Empty).StartsWith("System.Drawing", StringComparison.Ordinal)
        || (type.Namespace ?? string.Empty).StartsWith("Android", StringComparison.Ordinal)
        || (type.Namespace ?? string.Empty).StartsWith("SkiaSharp", StringComparison.Ordinal);

    [Fact]
    public void TheGuardReadsTheScanTypesAndIsNotVacuous()
    {
        var names = ScanTypes().Select(type => type.Name).ToList();

        Assert.Contains(nameof(ScanConfirmModel), names);
        Assert.Contains(nameof(IRecipeKeeper), names);
        Assert.Contains(nameof(IRecipeWordJudge), names);
        Assert.Contains(nameof(UncertainWord), names);
        Assert.True(names.Count >= 15, "Too few scan types found: " + names.Count);
    }

    [Fact]
    public void NoScanTypeCanHoldAPicture()
    {
        var offences = ScanTypes().SelectMany(Offences).ToList();

        Assert.True(offences.Count == 0, "A scan type can hold a picture:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void ThePayloadAndTheKeeperTakeWordsAndACardOnly()
    {
        var keep = typeof(IRecipeKeeper).GetMethod(nameof(IRecipeKeeper.Keep))!;
        var judge = typeof(IRecipeWordJudge).GetMethod(nameof(IRecipeWordJudge.Judge))!;

        Assert.Equal([typeof(Recipe)], keep.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal([typeof(RecipeReadPayload)], judge.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    private sealed class PlantedBitmapHolder
    {
        public byte[] Pixels { get; set; } = [];

        public Uri? Source { get; set; }

        public Stream? Frames { get; set; }

        public IReadOnlyList<byte[]> Pages { get; set; } = [];
    }

    private sealed class PlantedPhotoWord
    {
        public string Photo { get; set; } = string.Empty;
    }

    [Fact]
    public void TheDetectorFiresOnPlantedPictures()
    {
        var holder = Offences(typeof(PlantedBitmapHolder));

        Assert.Contains(holder, line => line.Contains("Pixels", StringComparison.Ordinal));
        Assert.Contains(holder, line => line.Contains("Uri", StringComparison.Ordinal));
        Assert.Contains(holder, line => line.Contains("Stream", StringComparison.Ordinal));
        Assert.Contains(holder, line => line.Contains("Pages", StringComparison.Ordinal));
        Assert.NotEmpty(Offences(typeof(PlantedPhotoWord)));
    }
}
