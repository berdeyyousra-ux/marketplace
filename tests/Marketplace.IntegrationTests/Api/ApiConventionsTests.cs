using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Marketplace.Api.Infrastructure;
using Marketplace.Domain.Common;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

public enum SampleKind
{
    Ebook,
    Template,
}

public sealed record EchoRequest(string Name, string Password, SampleKind Kind);

public sealed class EchoRequestValidator : AbstractValidator<EchoRequest>
{
    public EchoRequestValidator() => RuleFor(request => request.Name).MinimumLength(2).WithMessage("Name must be at least 2 characters.");
}

// A test-only controller, added to the app as an application part, exercising the shared API conventions.
[ApiController]
[AllowAnonymous]
[Route("__test/conventions")]
public sealed class ConventionsTestController : ControllerBase
{
    [HttpPost("echo")]
    public IActionResult Echo(EchoRequest request) => Ok(request);

    [HttpGet("conflict")]
    public IActionResult GetConflict() =>
        this.ToActionResult(Result.Failure<int>(new Error("already_owned", "Already owned.", ErrorType.Conflict)), value => Ok(value));
}

// Sections 8.1 and 11.2: JSON conventions, binding errors, validation errors and string normalization.
public sealed class ApiConventionsTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Echo_ValidRequest_TrimsAndNormalizesButKeepsPasswordSpaces()
    {
        using var client = Client();

        var response = await client.PostAsync("/__test/conventions/echo", Json("""{"name":"  Café  ","password":"  pass word  ","kind":"Template"}"""), CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("name").GetString().ShouldBe("Café");
        body.GetProperty("password").GetString().ShouldBe("  pass word  ");
        body.GetProperty("kind").GetString().ShouldBe("Template");
    }

    [Fact]
    public async Task Echo_PasswordWithDecomposedCharacter_IsNormalizedToNfc()
    {
        using var client = Client();

        var response = await client.PostAsync("/__test/conventions/echo", Json("""{"name":"Ana","password":"Café","kind":"Ebook"}"""), CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        body.GetProperty("password").GetString().ShouldBe("Café");
    }

    [Theory]
    [InlineData("""{"name":"Ana","password":"x","kind":"Ebook","isAdmin":true}""")]
    [InlineData("""{"name":"Ana","password":"x","kind":"NotAKind"}""")]
    [InlineData("""{"name":"Ana",""")]
    public async Task Echo_UnreadableBody_Returns400InvalidRequest(string json)
    {
        using var client = Client();

        var response = await client.PostAsync("/__test/conventions/echo", Json(json), CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().ShouldBe("invalid_request");
    }

    [Fact]
    public async Task Echo_FailsValidation_Returns422WithFieldErrors()
    {
        using var client = Client();

        var response = await client.PostAsync("/__test/conventions/echo", Json("""{"name":" A ","password":"x","kind":"Ebook"}"""), CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").GetProperty("name")[0].GetString().ShouldBe("Name must be at least 2 characters.");
    }

    [Fact]
    public async Task FailedResult_ReturnsProblemDetailsWithMappedStatus()
    {
        using var client = Client();

        var response = await client.GetAsync("/__test/conventions/conflict", CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("code").GetString().ShouldBe("already_owned");
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private HttpClient Client() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ConventionsTestController).Assembly);
            services.AddSingleton<IValidator<EchoRequest>, EchoRequestValidator>();
        })).CreateClient();
}
