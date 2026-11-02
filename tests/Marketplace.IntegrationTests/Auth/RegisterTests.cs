using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Infrastructure.Identity;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// EP-01 and US-01: always 202, with no difference that reveals whether an email is registered.
public sealed class RegisterTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private const string _password = "correct horse battery";

    [Fact]
    public async Task US01_NewEmail_CreatesUnconfirmedUserAndQueuesVerification()
    {
        using var client = AsAnonymous();

        var response = await RegisterAsync(client, "new.buyer@example.test", _password);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var user = await WithDbAsync(db => db.Users.Where(user => user.Email != MarketplaceWebFactory.AdminEmail).SingleAsync(CancellationToken));
        user.EmailConfirmed.ShouldBeFalse();
        user.DisplayName.ShouldBe("Buyer");
        user.CreatedAt.ShouldBe(Time.GetUtcNow());
        var message = await WithDbAsync(db => db.OutboxMessages.SingleAsync(CancellationToken));
        message.Type.ShouldBe(OutboxMessageTypes.EmailVerificationRequested);
        message.Payload.ShouldContain(user.Id.ToString());
        message.Payload.ShouldNotContain(_password);
    }

    [Fact]
    public async Task US01_ExistingEmailInAnyCase_CreatesNothingAndQueuesNotice()
    {
        using var client = AsAnonymous();
        await RegisterAsync(client, "existing@example.test", _password);

        var response = await RegisterAsync(client, "EXISTING@Example.test", "a different passphrase");

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await WithDbAsync(db => db.Users.Where(user => user.Email != MarketplaceWebFactory.AdminEmail).CountAsync(CancellationToken))).ShouldBe(1);
        var types = await WithDbAsync(db => db.OutboxMessages.OrderBy(message => message.CreatedAt).Select(message => message.Type).ToListAsync(CancellationToken));
        types.ShouldBe([OutboxMessageTypes.EmailVerificationRequested, OutboxMessageTypes.AccountExistsNotice]);
    }

    [Fact]
    public async Task US01_CommonPassword_Returns422ForNewAndExistingEmailAlike()
    {
        using var client = AsAnonymous();
        await RegisterAsync(client, "taken@example.test", _password);

        var forExisting = await RegisterAsync(client, "taken@example.test", "1234567890");
        var forNew = await RegisterAsync(client, "fresh@example.test", "1234567890");

        forExisting.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        forNew.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await forNew.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        problem.GetProperty("errors").GetProperty("password")[0].GetString()!.ShouldContain("too common");
    }

    [Theory]
    [InlineData("short", "Buyer")]
    [InlineData(_password, "B")]
    public async Task US01_InvalidFields_Return422ValidationFailed(string password, string displayName)
    {
        using var client = AsAnonymous();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email = "x@example.test", password, displayName }, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await WithDbAsync(db => db.Users.Where(user => user.Email != MarketplaceWebFactory.AdminEmail).CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task US01_StoredHash_IsPbkdf2Sha512WithAtLeast210000Iterations()
    {
        using var client = AsAnonymous();
        await RegisterAsync(client, "hash@example.test", _password);

        var hash = Convert.FromBase64String(await WithDbAsync(db => db.Users.Where(user => user.Email != MarketplaceWebFactory.AdminEmail).Select(user => user.PasswordHash!).SingleAsync(CancellationToken)));

        hash[0].ShouldBe((byte)0x01);
        BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4)).ShouldBe(2u);
        BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4)).ShouldBeGreaterThanOrEqualTo(210_000u);
    }

    [Fact]
    public async Task US01_SameEmailConcurrently_CreatesOneUserAndBothReturn202()
    {
        using var client = AsAnonymous();

        var responses = await Task.WhenAll(
            RegisterAsync(client, "race@example.test", _password),
            RegisterAsync(client, "race@example.test", _password));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Accepted);
        (await WithDbAsync(db => db.Users.Where(user => user.Email != MarketplaceWebFactory.AdminEmail).CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public void CommonPasswordList_IsEmbedded()
    {
        EmbeddedCommonPasswords.Count.ShouldBeGreaterThan(9000);
        new EmbeddedCommonPasswords().Contains("QWERTY").ShouldBeTrue();
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName = "Buyer" }, CancellationToken);
}
