using System.Reflection;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Marketplace.ArchitectureTests;

// D-41: Application reaches the database only through IAppDbContext, and there are no repository classes.
public sealed class PersistenceRulesTests
{
    private static readonly Assembly[] _sourceAssemblies =
    [
        Assembly.Load("Marketplace.Domain"),
        Assembly.Load("Marketplace.Application"),
        Assembly.Load("Marketplace.Infrastructure"),
        Assembly.Load("Marketplace.Api"),
        Assembly.Load("Marketplace.Worker"),
    ];

    [Fact]
    public void AppDbContext_Implements_IAppDbContext()
    {
        typeof(IAppDbContext).IsAssignableFrom(typeof(AppDbContext)).ShouldBeTrue();
        typeof(IAppDbContext).Assembly.GetName().Name.ShouldBe("Marketplace.Application");
    }

    [Fact]
    public void IAppDbContext_EveryEntitySet_HasMatchingAppDbContextSet()
    {
        var interfaceSets = typeof(IAppDbContext).GetProperties().Select(property => (property.Name, property.PropertyType));
        var contextSets = typeof(AppDbContext).GetProperties().Select(property => (property.Name, property.PropertyType)).ToHashSet();

        interfaceSets.Except(contextSets).ShouldBeEmpty();
    }

    [Fact]
    public void SourceAssemblies_DeclareNoRepositoryTypes()
    {
        var repositories = _sourceAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName);

        repositories.ShouldBeEmpty();
    }
}
