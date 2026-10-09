using System.Reflection;
using Pulp.Domain.Children;
using Pulp.Domain.Services;

namespace Pulp.Tests.HandOff;

/// <summary>
/// S1 guard (D210, D249): whether a child may start a title on a service is read from the catalogue
/// entry and nothing else. A caller-built <see cref="StreamingService"/> — a public entry re-badged
/// kids-only, or an attested id paired with another app's package — must have no way in, so no
/// public member of the child gate accepts one, and nothing outside the constructor can set
/// <see cref="StreamingService.KidsOnly"/>.
/// </summary>
public class ChildGateTakesNoServiceRecordTests
{
    [Fact]
    public void NoPublicMemberOfTheChildGateTakesAServiceRecord()
    {
        var methods = new[] { typeof(ChildFilter), typeof(ChildHandOff), typeof(AttestedServices) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .ToList();
        Assert.Contains(methods, method => method.Name == nameof(ChildFilter.CanStart));
        Assert.Contains(methods, method => method.Name == nameof(ChildFilter.BadgeFor));
        Assert.Contains(methods, method => method.Name == nameof(ChildHandOff.For));
        Assert.Contains(methods, method => method.Name == nameof(AttestedServices.Covers));

        var offenders = methods
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(StreamingService)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Takes a caller-built service record: " + string.Join(", ", offenders));
    }

    [Fact]
    public void KidsOnlyCannotBeSetOutsideTheConstructor()
    {
        var property = typeof(StreamingService).GetProperty(nameof(StreamingService.KidsOnly));

        Assert.NotNull(property);
        Assert.Null(property.SetMethod);
    }
}
