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
            // API responses allow nothing. The docs UI (EP-92) needs its own scripts, styles and fonts.
            headers.ContentSecurityPolicy = Infrastructure.ApiDocs.IsDocsRequest(context.Request.Path)
                ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'"
                : "default-src 'none'";
            return Task.CompletedTask;
        });

        await next(context);
    }
}
