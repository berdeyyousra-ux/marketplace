using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.Errors;
using Marketplace.Domain.Common;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

// T-022, sections 6.1, 8.1 and 8.2: errors are ProblemDetails with code and traceId, and never expose internals.
public sealed class ProblemDetailsTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task UnhandledException_Returns500ProblemDetailsWithoutStackTrace()
    {
        using var client = ClientWithTestEndpoints();

        var response = await client.GetAsync("/__test/throw", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("internal_error");
        problem.GetProperty("type").GetString().ShouldBe("https://marketplace.dev/errors/internal_error");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        body.ShouldNotContain("secret-detail-from-exception");
        body.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task UnhandledException_KeepsCorrelationAndSecurityHeaders()
    {
        using var client = ClientWithTestEndpoints();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "t022-correlation");

        var response = await client.GetAsync("/__test/throw", CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("t022-correlation");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task FM13_UniqueViolation_Returns409NotA500()
    {
        using var client = ClientWithTestEndpoints();

        var response = await client.GetAsync("/__test/unique-violation", CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("already_seller");
        problem.ToString().ShouldNotContain("ux_seller_profiles_user_id");
    }

    [Fact]
    public async Task TransientDatabaseFailure_Returns503()
    {
        using var client = ClientWithTestEndpoints();

        var response = await client.GetAsync("/__test/database-down", CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        problem.GetProperty("code").GetString().ShouldBe("service_unavailable");
    }

    [Theory]
    [InlineData(ErrorType.Validation, 422)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.PreconditionFailed, 412)]
    [InlineData(ErrorType.Locked, 423)]
    [InlineData(ErrorType.Unavailable, 503)]
    public async Task DomainError_MapsToSection61Status(ErrorType type, int status)
    {
        using var client = ClientWithTestEndpoints();

        var response = await client.GetAsync($"/__test/error/{type}", CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        ((int)response.StatusCode).ShouldBe(status);
        problem.GetProperty("code").GetString().ShouldBe("sample_error");
        problem.GetProperty("title").GetString().ShouldBe("Sample error");
        problem.GetProperty("detail").GetString().ShouldBe("Sample detail.");
        problem.GetProperty("status").GetInt32().ShouldBe(status);
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        using var client = AsAnonymous();

        var response = await client.GetAsync("/api/v1/does-not-exist", CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("code").GetString().ShouldBe("not_found");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
    }

    private HttpClient ClientWithTestEndpoints() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddTransient<IStartupFilter, TestEndpoints>()))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Appended after the app's own pipeline, so requests pass through the real exception handler first.
    private sealed class TestEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__test/throw", branch => branch.Run(_ => throw new InvalidOperationException("secret-detail-from-exception")));
            app.Map("/__test/unique-violation", branch => branch.Run(_ => throw new DbUpdateException(
                "Duplicate", new PostgresException("duplicate key", "ERROR", "ERROR", "23505", constraintName: "ux_seller_profiles_user_id"))));
            app.Map("/__test/database-down", branch => branch.Run(_ => throw new TimeoutException("Database timed out")));
            app.Map("/__test/error", branch => branch.Run(async context =>
            {
                var type = Enum.Parse<ErrorType>(context.Request.Path.Value!.Trim('/'));
                var problem = ApiProblems.Create(context, new Error("sample_error", "Sample detail.", type));
                context.Response.StatusCode = problem.Status!.Value;
                await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
            }));
        };
    }
}
