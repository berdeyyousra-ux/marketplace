using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Marketplace.IntegrationTests.Persistence;

// FM-13: a unique-constraint violation becomes a 409 error, never a 500.
public sealed class DbExceptionTranslatorTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly DbExceptionTranslator _translator = new(NullLogger<DbExceptionTranslator>.Instance);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task FM13_SecondSellerProfile_TranslatesToAlreadySeller()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new AppUser { UserName = "fm13@example.test", Email = "fm13@example.test", DisplayName = "Seller", CreatedAt = _now };
        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            db.SellerProfiles.Add(SellerProfile.Create(user.Id, "First", null, "ES", _now).Value);
            await db.SaveChangesAsync(ct);
        }

        await using var second = CreateContext();
        second.SellerProfiles.Add(SellerProfile.Create(user.Id, "Second", null, "ES", _now).Value);
        var exception = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync(ct));

        var error = _translator.Translate(exception);

        error.ShouldNotBeNull();
        error.Code.ShouldBe("already_seller");
        error.Message.ShouldNotContain("ux_seller_profiles_user_id");
    }

    [Fact]
    public async Task Translate_ForeignKeyViolation_ReturnsNullSoItBecomesA500()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        db.SellerProfiles.Add(SellerProfile.Create(Guid.CreateVersion7(), "Orphan", null, "ES", _now).Value);
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        _translator.Translate(exception).ShouldBeNull();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
}
