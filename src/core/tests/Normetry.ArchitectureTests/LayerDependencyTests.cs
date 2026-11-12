using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Normetry.ArchitectureTests;

// NetArchTest inspects type-level dependencies, so a rule fails as soon as a type uses a forbidden namespace.
public class LayerDependencyTests
{
    [Fact]
    public void BuildingBlocks_depends_on_no_Normetry_project_EF_Core_or_ASP_NET_Core() =>
        AssertNoDependency(
            typeof(BuildingBlocks.AssemblyMarker).Assembly,
            "Normetry.Domain",
            "Normetry.Application",
            "Normetry.Infrastructure",
            "Normetry.Api",
            "Normetry.ServiceDefaults",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore"
        );

    // The dispatcher lives in BuildingBlocks, which Domain references, so it is named explicitly.
    [Fact]
    public void Domain_depends_only_on_BuildingBlocks_and_not_on_the_dispatcher() =>
        AssertNoDependency(
            typeof(Domain.AssemblyMarker).Assembly,
            "Normetry.BuildingBlocks.Cqrs",
            "Normetry.Application",
            "Normetry.Infrastructure",
            "Normetry.Api",
            "Normetry.ServiceDefaults",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore"
        );

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_Api_or_EF_Core() =>
        AssertNoDependency(
            typeof(Application.AssemblyMarker).Assembly,
            "Normetry.Infrastructure",
            "Normetry.Api",
            "Microsoft.EntityFrameworkCore"
        );

    [Fact]
    public void Infrastructure_does_not_depend_on_Api() =>
        AssertNoDependency(typeof(Infrastructure.AssemblyMarker).Assembly, "Normetry.Api");

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types
            .InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.NotEmpty(result.SelectedTypesForTesting);
        Assert.True(
            result.IsSuccessful,
            $"{assembly.GetName().Name} has forbidden dependencies: "
                + string.Join(
                    "; ",
                    result.FailingTypes.Select(t => $"{t.FullName} ({t.Explanation})")
                )
        );
    }
}
