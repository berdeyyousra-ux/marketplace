using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marketplace.Api.Errors;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Api.Infrastructure;

// D-21 and section 10.1: marks an endpoint that needs an Idempotency-Key header.
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : TypeFilterAttribute
{
    public IdempotentAttribute()
        : base(typeof(IdempotencyFilter))
    {
    }
}

public sealed class IdempotencyFilter(IIdempotencyStore store, ICurrentUser currentUser) : IAsyncResourceFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public const int MaxBodyBytes = 64 * 1024;

    private static readonly Error _keyRequired =
        new("idempotency_key_required", "This request needs an Idempotency-Key header.", ErrorType.Validation);

    private static readonly Error _invalidKey =
        new("invalid_request", "Idempotency-Key must be 1 to 64 characters of letters, digits, '-' or '_'.", ErrorType.Validation);

    private static readonly Error _bodyTooLarge =
        new("invalid_request", "Idempotent request bodies can be at most 64 KB.", ErrorType.Validation);

    private static readonly Error _keyReuse =
        new("idempotency_key_reuse", "This Idempotency-Key was already used for a different request.", ErrorType.Validation);

    private static readonly Error _inProgress =
        new("idempotency_in_progress", "A request with this Idempotency-Key is still being processed.", ErrorType.Conflict);

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (key.Length == 0)
        {
            context.Result = Problem(http, _keyRequired, StatusCodes.Status400BadRequest);
            return;
        }

        if (key.Length > 64 || !key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            context.Result = Problem(http, _invalidKey, StatusCodes.Status400BadRequest);
            return;
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Idempotent endpoints require an authenticated caller.");
        var body = await ReadBodyAsync(http.Request);
        if (body is null)
        {
            context.Result = Problem(http, _bodyTooLarge, StatusCodes.Status400BadRequest);
            return;
        }

        var path = http.Request.Path.Value ?? string.Empty;
        var request = new IdempotencyRequest(userId, key, http.Request.Method, path, Hash(http.Request.Method, path, body));
        var outcome = await store.ClaimAsync(request, http.RequestAborted);

        switch (outcome.Claim)
        {
            case IdempotencyClaim.Mismatch:
                context.Result = Problem(http, _keyReuse, StatusCodes.Status422UnprocessableEntity);
                return;
            case IdempotencyClaim.InProgress:
                http.Response.Headers.RetryAfter = "1";
                context.Result = Problem(http, _inProgress, StatusCodes.Status409Conflict);
                return;
            case IdempotencyClaim.Replay:
                // Replays carry no Location; clients read the id from the body (section 10.1).
                http.Response.Headers[ReplayedHeader] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = outcome.ResponseStatus,
                    Content = outcome.ResponseBody,
                    ContentType = outcome.ResponseStatus >= 400 ? "application/problem+json" : "application/json",
                };
                return;
            case IdempotencyClaim.Proceed:
            default:
                await ExecuteAndRecordAsync(context, next, userId, key);
                return;
        }
    }

    // 2xx and 4xx responses are stored for replay. A 5xx or an exception releases the key so a retry can run.
    private async Task ExecuteAndRecordAsync(ResourceExecutingContext context, ResourceExecutionDelegate next, Guid userId, string key)
    {
        var response = context.HttpContext.Response;
        var original = response.Body;
        await using var capture = new MemoryStream();
        response.Body = capture;
        try
        {
            var executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                await store.ReleaseAsync(userId, key, CancellationToken.None);
                return;
            }

            if (response.StatusCode >= 500)
            {
                await store.ReleaseAsync(userId, key, CancellationToken.None);
            }
            else
            {
                await store.CompleteAsync(userId, key, response.StatusCode, Encoding.UTF8.GetString(capture.ToArray()), CancellationToken.None);
            }
        }
        catch
        {
            await store.ReleaseAsync(userId, key, CancellationToken.None);
            throw;
        }
        finally
        {
            response.Body = original;
            capture.Position = 0;
            await capture.CopyToAsync(original, context.HttpContext.RequestAborted);
        }
    }

    // Reads the body (at most 64 KB) and rewinds it for model binding. Returns null when it is too large.
    private static async Task<string?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        request.EnableBuffering(MaxBodyBytes + 1);
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total), request.HttpContext.RequestAborted)) > 0)
        {
            total += read;
        }

        request.Body.Position = 0;
        return total > MaxBodyBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    // METHOD, newline, path, newline, then the body re-serialized with sorted property names and no whitespace.
    public static string Hash(string method, string path, string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{method}\n{path}\n{Canonical(body)}")));

    private static string Canonical(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            return Sort(JsonNode.Parse(body))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, Sort(pair.Value?.DeepClone())))),
        JsonArray array => new JsonArray(array.Select(item => Sort(item?.DeepClone())).ToArray()),
        _ => node?.DeepClone(),
    };

    private static ObjectResult Problem(HttpContext http, Error error, int status) =>
        new(ApiProblems.Create(http, error, status)) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
