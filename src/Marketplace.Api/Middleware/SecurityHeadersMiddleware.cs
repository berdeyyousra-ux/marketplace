namespace Marketplace.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'";

        await next(context);
    }
}
