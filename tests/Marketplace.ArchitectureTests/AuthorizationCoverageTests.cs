using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace Marketplace.ArchitectureTests;

// Section 11.6: every action states its access rule explicitly.
public sealed class AuthorizationCoverageTests
{
    private static readonly Type[] _controllers = Assembly.Load("Marketplace.Api").GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
        .ToArray();

    [Fact]
    public void AuthorizationMatrix_AllEndpointsCovered()
    {
        var actions = _controllers.SelectMany(controller => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null)
            .Select(method => (Controller: controller, Action: method)));

        var uncovered = actions
            .Where(entry => !Declares(entry.Action) && !Declares(entry.Controller))
            .Select(entry => $"{entry.Controller.Name}.{entry.Action.Name}");

        _controllers.ShouldNotBeEmpty();
        uncovered.ShouldBeEmpty();
    }

    // A class-level [AllowAnonymous] overrides [Authorize] on its actions, which once almost exposed change-password.
    [Fact]
    public void Controllers_DoNotDeclareAllowAnonymousAtClassLevel()
    {
        var offenders = _controllers.Where(controller => controller.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any());

        offenders.Select(controller => controller.Name).ShouldBeEmpty();
    }

    private static bool Declares(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(attribute => attribute is IAuthorizeData or IAllowAnonymous);
}
