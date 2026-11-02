using System.Net;
using Marketplace.Application.Ops;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

// T-015 acceptance: one smoke test through the shared factory, plus checks that the fixture behaves as section 14.2 says.
public sealed class SmokeTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Health_SharedFactory_LiveAndReadyReturn200()
    {
        using var client = AsAnonymous();

        var live = await client.GetAsync("/health/live", CancellationToken);
        var ready = await client.GetAsync("/health/ready", CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetDatabase_AfterWrites_EmptiesTablesButKeepsReferenceData()
    {
        await WithDbAsync(async db =>
        {
            db.AuditLog.Add(new AuditLogEntry { OccurredAt = Time.GetUtcNow(), ActorType = AuditActorType.System, Action = "test.write" });
            return await db.SaveChangesAsync(CancellationToken);
        });

        await ResetDatabaseAsync();

        (await WithDbAsync(db => db.AuditLog.CountAsync(CancellationToken))).ShouldBe(0);
        (await WithDbAsync(db => db.Categories.CountAsync(CancellationToken))).ShouldBe(8);
    }

    [Fact]
    public void TimeProvider_InApp_IsTheFakeClock()
    {
        var before = Time.GetUtcNow();

        Time.Advance(TimeSpan.FromHours(1));
        var appTime = Factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

        appTime.ShouldBe(before.AddHours(1));
    }
}
