using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Marketplace.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly _domain = Assembly.Load("Marketplace.Domain");
    private static readonly Assembly _application = Assembly.Load("Marketplace.Application");
    private static readonly Assembly _api = Assembly.Load("Marketplace.Api");

    [Fact]
    public void Domain_ReferencedAssemblies_AreBaseClassLibraryOnly()
    {
        var references = _domain.GetReferencedAssemblies().Select(reference => reference.Name!);

        var nonFrameworkReferences = references.Where(name => !IsBaseClassLibrary(name));

        nonFrameworkReferences.ShouldBeEmpty();
    }

    [Fact]
    public void Application_ReferencedAssemblies_ExcludeInfrastructureAndProviders()
    {
        string[] forbiddenPrefixes = ["Marketplace.Infrastructure", "Npgsql", "Stripe", "Azure", "MailKit"];

        var references = _application.GetReferencedAssemblies().Select(reference => reference.Name!);

        var forbiddenReferences = references.Where(
            name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        forbiddenReferences.ShouldBeEmpty();
    }

    [Fact]
    public void Application_Types_HaveNoDependencyOnInfrastructureOrProviders()
    {
        var result = Types.InAssembly(_application)
            .ShouldNot()
            .HaveDependencyOnAny("Marketplace.Infrastructure", "Npgsql", "Stripe", "Azure", "MailKit")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Controllers_ActionSignatures_DoNotExposeDomainTypes()
    {
        var actions = _api.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        var violations = actions
            .Where(ExposesDomainType)
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}");

        violations.ShouldBeEmpty();
    }

    private static bool IsBaseClassLibrary(string assemblyName) =>
        assemblyName is "mscorlib" or "netstandard" || assemblyName.StartsWith("System", StringComparison.Ordinal);

    private static bool ExposesDomainType(MethodInfo action) =>
        action.GetParameters().Select(parameter => parameter.ParameterType)
            .Append(action.ReturnType)
            .Any(IsOrContainsDomainType);

    private static bool IsOrContainsDomainType(Type type)
    {
        if (type.Assembly == _domain)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return IsOrContainsDomainType(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsOrContainsDomainType);
    }
}
