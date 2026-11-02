namespace Marketplace.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Set when the response starts, so error responses written by the exception handler keep them too.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = "default-src 'none'";
            return Task.CompletedTask;
        });

        await next(context);
    }
}
