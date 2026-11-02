using System.Buffers.Binary;
using Marketplace.Api.Errors;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Api.Infrastructure;

// Section 8.1: the ETag is the base64url of the xmin version, quoted.
public static class ETags
{
    public static string Format(uint version)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);
        return "\"" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "\"";
    }

    public static bool TryParse(string? header, out uint version)
    {
        version = 0;
        if (header is not { Length: > 2 } || header[0] != '"' || header[^1] != '"')
        {
            return false;
        }

        var base64 = header[1..^1].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        Span<byte> bytes = stackalloc byte[8];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != 4)
        {
            return false;
        }

        version = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    // The expected version from If-Match. Requires RequireIfMatchAttribute on the action.
    public static uint IfMatchVersion(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return (uint)controller.HttpContext.Items[RequireIfMatchAttribute.ItemKey]!;
    }
}

// Missing If-Match is 428 precondition_required, and an unreadable one is 412 precondition_failed (section 8.2).
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : Attribute, IActionFilter
{
    public const string ItemKey = "IfMatchVersion";

    private static readonly Error _required =
        new("precondition_required", "This request needs an If-Match header with the product's ETag.", ErrorType.PreconditionFailed);

    private static readonly Error _failed =
        new("precondition_failed", "The If-Match header does not match the current version.", ErrorType.PreconditionFailed);

    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var header = context.HttpContext.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(header))
        {
            context.Result = Problem(context.HttpContext, _required, StatusCodes.Status428PreconditionRequired);
        }
        else if (!ETags.TryParse(header, out var version))
        {
            context.Result = Problem(context.HttpContext, _failed, StatusCodes.Status412PreconditionFailed);
        }
        else
        {
            context.HttpContext.Items[ItemKey] = version;
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private static ObjectResult Problem(HttpContext context, Error error, int status) =>
        new(ApiProblems.Create(context, error, status)) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
