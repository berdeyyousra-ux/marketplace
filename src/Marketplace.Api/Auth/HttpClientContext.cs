using System.Net;
using Marketplace.Application.Ops;

namespace Marketplace.Api.Auth;

// The client IP comes from forwarded headers, which trust only the known proxies (section 11.4).
public sealed class HttpClientContext(IHttpContextAccessor accessor) : IClientContext
{
    private const int _userAgentMaxLength = 300;

    public IPAddress? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress;

    public string? UserAgent
    {
        get
        {
            string? agent = accessor.HttpContext?.Request.Headers.UserAgent;
            return agent is { Length: > _userAgentMaxLength } ? agent[.._userAgentMaxLength] : agent;
        }
    }
}
