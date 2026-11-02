using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Api.Infrastructure;
using Marketplace.Application.Ops;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

public sealed record IdempotentSample(string Name);

// A test-only controller with the [Idempotent] filter, added to a derived host as an application part.
[ApiController]
[Authorize]
[Route("__test/idempotent")]
public sealed class IdempotentTestController(Marketplace.Application.Abstractions.IAppDbContext db) : ControllerBase
{
    [HttpPost("create")]
    [Idempotent]
    public async Task<IActionResult> CreateAsync(IdempotentSample sample)
    {
        db.AuditLog.Add(new AuditLogEntry { OccurredAt = DateTimeOffset.UtcNow, ActorType = AuditActorType.System, Action = "test.idempotent", Details = JsonSerializer.Serialize(sample) });
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        var id = Guid.CreateVersion7();
        return Created($"/__test/idempotent/{id}", new { id, sample.Name });
    }

    [HttpPost("slow")]
    [Idempotent]
    public async Task<IActionResult> SlowAsync(IdempotentSample sample)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800), HttpContext.RequestAborted);
        return Created("/__test/idempotent/slow", sample);
    }

    [HttpPost("fail")]
    [Idempotent]
    public IActionResult Fail(IdempotentSample sample) => throw new InvalidOperationException($"Simulated failure {sample.Name} on {HttpContext.TraceIdentifier}");

    [HttpPost("conflict")]
    [Idempotent]
    public IActionResult Conflict(IdempotentSample sample) =>
        this.Problem(new Marketplace.Domain.Common.Error("already_owned", "Owned " + sample.Name, Marketplace.Domain.Common.ErrorType.Conflict));
}

// T-032, section 10.1, FM-01 to FM-03.
public sealed class IdempotencyTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task FM01_SameKeyAndBody_ReplaysResponseWithoutSecondEffect()
    {
        using var client = await ClientAsync("fm01@example.test");

        var first = await PostAsync(client, "create", "key-fm01", """{"name":"Ana"}""");
        var second = await PostAsync(client, "create", "key-fm01", """{"name":"Ana"}""");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Location.ShouldNotBeNull();
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues(IdempotencyFilter.ReplayedHeader).Single().ShouldBe("true");
        second.Headers.Location.ShouldBeNull();
        (await second.Content.ReadAsStringAsync(CancellationToken)).ShouldBe(await first.Content.ReadAsStringAsync(CancellationToken));
        (await EffectCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task FM02_SameKeyDifferentBody_Returns422()
    {
        using var client = await ClientAsync("fm02@example.test");
        await PostAsync(client, "create", "key-fm02", """{"name":"Ana"}""");

        var reuse = await PostAsync(client, "create", "key-fm02", """{"name":"Bea"}""");

        await ShouldHaveCodeAsync(reuse, HttpStatusCode.UnprocessableEntity, "idempotency_key_reuse");
        (await EffectCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task FM03_ConcurrentSameKey_OneExecutesOtherGetsInProgress()
    {
        using var client = await ClientAsync("fm03@example.test");

        var responses = await Task.WhenAll(
            PostAsync(client, "slow", "key-fm03", """{"name":"Ana"}"""),
            PostAsync(client, "slow", "key-fm03", """{"name":"Ana"}"""));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        conflict.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(1));
        await ShouldHaveCodeAsync(conflict, HttpStatusCode.Conflict, "idempotency_in_progress");
    }

    [Theory]
    [InlineData(null, "idempotency_key_required")]
    [InlineData("has spaces", "invalid_request")]
    public async Task MissingOrInvalidKey_Returns400(string? key, string code)
    {
        using var client = await ClientAsync($"key-check-{code}@example.test".Replace("_", "-", StringComparison.Ordinal));

        var response = await PostAsync(client, "create", key, """{"name":"Ana"}""");

        await ShouldHaveCodeAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task ReorderedJsonWithWhitespace_IsTheSameRequest()
    {
        using var client = await ClientAsync("canonical@example.test");
        await PostAsync(client, "create", "key-canonical", """{"name":"Ana"}""");

        var replay = await PostAsync(client, "create", "key-canonical", """{ "name" :  "Ana" }""");

        replay.Headers.GetValues(IdempotencyFilter.ReplayedHeader).Single().ShouldBe("true");
    }

    [Fact]
    public async Task ClientError_IsStoredAndReplayed()
    {
        using var client = await ClientAsync("stored-4xx@example.test");

        var first = await PostAsync(client, "conflict", "key-4xx", """{"name":"Ana"}""");
        var second = await PostAsync(client, "conflict", "key-4xx", """{"name":"Ana"}""");

        first.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.Headers.GetValues(IdempotencyFilter.ReplayedHeader).Single().ShouldBe("true");
    }

    [Fact]
    public async Task ServerError_ReleasesKeySoARetryRuns()
    {
        using var client = await ClientAsync("released@example.test");

        var first = await PostAsync(client, "fail", "key-5xx", """{"name":"Ana"}""");
        var retry = await PostAsync(client, "fail", "key-5xx", """{"name":"Ana"}""");

        first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retry.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retry.Headers.Contains(IdempotencyFilter.ReplayedHeader).ShouldBeFalse();
        (await WithDbAsync(db => db.IdempotencyKeys.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task CrashedRequestLock_IsTakenOverAfterExpiryButNotBefore()
    {
        using var client = await ClientAsync("takeover@example.test");
        var userId = await WithDbAsync(db => db.Users.Where(user => user.Email == "takeover@example.test").Select(user => user.Id).SingleAsync(CancellationToken));
        var hash = IdempotencyFilter.Hash("POST", "/__test/idempotent/create", """{"name":"Ana"}""");
        await WithDbAsync(db =>
        {
            db.IdempotencyKeys.Add(new IdempotencyKey
            {
                UserId = userId,
                Key = "key-crashed",
                Method = "POST",
                Path = "/__test/idempotent/create",
                RequestHash = hash,
                CreatedAt = Time.GetUtcNow(),
                LockedUntil = Time.GetUtcNow().AddSeconds(60),
                ExpiresAt = Time.GetUtcNow().AddHours(24),
            });
            return db.SaveChangesAsync(CancellationToken);
        });

        var whileLocked = await PostAsync(client, "create", "key-crashed", """{"name":"Ana"}""");
        Time.Advance(TimeSpan.FromSeconds(61));
        var afterExpiry = await PostAsync(client, "create", "key-crashed", """{"name":"Ana"}""");

        whileLocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        afterExpiry.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SameKeyForDifferentUsers_IsIndependent()
    {
        using var first = await ClientAsync("user-a@example.test");
        using var second = await ClientAsync("user-b@example.test");

        await PostAsync(first, "create", "shared-key", """{"name":"Ana"}""");
        var other = await PostAsync(second, "create", "shared-key", """{"name":"Bea"}""");

        other.StatusCode.ShouldBe(HttpStatusCode.Created);
        other.Headers.Contains(IdempotencyFilter.ReplayedHeader).ShouldBeFalse();
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        await CreateVerifiedUserAsync(email);
        var token = (await SignInAsync(email)).AccessToken;
        var client = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(IdempotentTestController).Assembly)))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string action, string? key, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/__test/idempotent/{action}") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyFilter.HeaderName, key);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Task<int> EffectCountAsync() =>
        WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "test.idempotent", CancellationToken));

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }
}
