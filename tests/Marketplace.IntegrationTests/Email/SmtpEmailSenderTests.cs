using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Email;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Email;

// D-27: the real MailKit sender against a Mailpit SMTP server, read back through Mailpit's HTTP API.
public sealed class SmtpEmailSenderTests : IAsyncLifetime
{
    private const int _smtpPort = 1025;
    private const int _httpPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.31")
        .WithPortBinding(_smtpPort, assignRandomHostPort: true)
        .WithPortBinding(_httpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(_httpPort).ForPath("/livez")))
        .Build();

    public async ValueTask InitializeAsync() => await _mailpit.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task SendAsync_ToMailpit_DeliversMessageWithConfiguredSender()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            SmtpHost = _mailpit.Hostname,
            SmtpPort = _mailpit.GetMappedPublicPort(_smtpPort),
            FromAddress = "noreply@marketplace.dev",
            FromName = "Marketplace",
        }));

        await sender.SendAsync(new EmailMessage("buyer@example.test", "Confirm your email address", "Hello from the test."), TestContext.Current.CancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(_httpPort)}") };
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages", TestContext.Current.CancellationToken);
        var message = list.GetProperty("messages")[0];
        message.GetProperty("Subject").GetString().ShouldBe("Confirm your email address");
        message.GetProperty("From").GetProperty("Address").GetString().ShouldBe("noreply@marketplace.dev");
        message.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("buyer@example.test");
    }
}
