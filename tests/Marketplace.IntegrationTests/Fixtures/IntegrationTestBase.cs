using System.Net.Http.Json;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Marketplace.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class IntegrationTestGroup : ICollectionFixture<MarketplaceWebFactory>
{
    public const string Name = "Integration";
}

// Every test class in the collection inherits this. Tests in one collection run one at a time,
// so the database is reset before each test and nothing is shared between tests.
[Collection(IntegrationTestGroup.Name)]
public abstract class IntegrationTestBase(MarketplaceWebFactory factory) : IAsyncLifetime
{
    // Section 14.2. TRUNCATE does not fire the row-level append-only triggers.
    private const string _resetSql = """
        DO $$ DECLARE t text; BEGIN
          FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public'
            AND tablename NOT IN ('__EFMigrationsHistory','categories','roles','AspNetRoles')
          LOOP EXECUTE 'TRUNCATE TABLE ' || quote_ident(t) || ' RESTART IDENTITY CASCADE'; END LOOP;
        END $$;
        """;

    protected MarketplaceWebFactory Factory { get; } = factory;

    protected FakeTimeProvider Time => Factory.Time;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The shared FakeTimeProvider can only move forward, so tests work relative to Time.GetUtcNow().
    public virtual async ValueTask InitializeAsync() => await ResetDatabaseAsync();

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public const string DefaultPassword = "correct horse battery";

    protected HttpClient AsAnonymous() => Factory.CreateClient();

    protected async Task<(string AccessToken, string RefreshToken)> SignInAsync(string email, string password = DefaultPassword)
    {
        using var client = AsAnonymous();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    protected async Task<(string AccessToken, string RefreshToken)> RefreshTokensAsync(string refreshToken)
    {
        using var client = AsAnonymous();
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken);
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    protected HttpClient WithToken(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    protected async Task<HttpClient> AsAdminAsync() =>
        WithToken((await SignInAsync(MarketplaceWebFactory.AdminEmail, MarketplaceWebFactory.AdminPassword)).AccessToken);

    // A verified, signed-in user with no roles.
    protected async Task<HttpClient> AsBuyerAsync(string email)
    {
        await CreateVerifiedUserAsync(email);
        return WithToken((await SignInAsync(email)).AccessToken);
    }

    // A verified user who became a seller, with a refreshed token that carries the Seller role.
    protected async Task<HttpClient> AsSellerAsync(string email, string displayName = "Studio Test")
    {
        await CreateVerifiedUserAsync(email);
        var tokens = await SignInAsync(email);
        using (var buyer = WithToken(tokens.AccessToken))
        {
            var created = await buyer.PostAsJsonAsync("/api/v1/sellers", new { displayName, bio = (string?)null, country = "ES" }, CancellationToken);
            created.EnsureSuccessStatusCode();
        }

        return WithToken((await RefreshTokensAsync(tokens.RefreshToken)).AccessToken);
    }

    // Creates, uploads a small PDF for, and publishes a product through the real API. Returns its ID.
    protected static async Task<Guid> CreatePublishedProductAsync(HttpClient seller, string title = "Notion Budget Template", string amount = "19.99")
    {
        var created = await seller.PostAsJsonAsync(
            "/api/v1/products", new { title, description = "A digital product.", price = new { amount, currency = "EUR" }, categoryId = 2 }, CancellationToken);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken)).GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\ntest product\n"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "product.pdf");
        (await seller.PostAsync($"/api/v1/products/{id}/file", form, CancellationToken)).EnsureSuccessStatusCode();

        var etag = (await seller.GetAsync($"/api/v1/products/{id}", CancellationToken)).Headers.ETag!.Tag;
        using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{id}/publish");
        publish.Headers.TryAddWithoutValidation("If-Match", etag);
        (await seller.SendAsync(publish, CancellationToken)).EnsureSuccessStatusCode();
        return id;
    }

    protected static Task<HttpResponseMessage> CreateOrderAsync(HttpClient buyer, IEnumerable<Guid> productIds, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(new { productIds }) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return buyer.SendAsync(request, CancellationToken);
    }

    // Creates a user through the real register and verify-email flow, then returns its ID.
    protected async Task<Guid> CreateVerifiedUserAsync(string email, string password = DefaultPassword, string displayName = "Test User")
    {
        using var client = AsAnonymous();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName }, CancellationToken);
        register.EnsureSuccessStatusCode();
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);
        var token = Factory.Services.GetRequiredService<FakeEmailSender>().LatestToken(email, "verify-email");
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token }, CancellationToken);
        verify.EnsureSuccessStatusCode();
        return await WithDbAsync(db => db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync(CancellationToken));
    }

    protected async Task ResetDatabaseAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(_resetSql, CancellationToken);

        // Section 14.2: the reset removes users, so the admin is seeded again.
        await Factory.Services.SeedAdminAsync(Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), CancellationToken);
    }

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
