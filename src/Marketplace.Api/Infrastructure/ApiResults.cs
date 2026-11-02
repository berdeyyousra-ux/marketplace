using System.Text.Json;
using FluentValidation;
using Marketplace.Api.Errors;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Api.Infrastructure;

public static class ApiResults
{
    public static readonly Error ValidationFailed =
        new("validation_failed", "One or more fields are invalid.", ErrorType.Validation);

    // Turns a handler result into a response: the success callback on success, otherwise ProblemDetails.
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : controller.Problem(result.Error);
    }

    public static ObjectResult Problem(this ControllerBase controller, Error error, IDictionary<string, string[]>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var problem = ApiProblems.Create(controller.HttpContext, error);
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }

    // [ApiController] binding failures (malformed JSON, unknown members, wrong types) are 400 invalid_request.
    public static IActionResult InvalidModelState(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = ApiProblems.Create(context.HttpContext, ApiProblems.InvalidRequest, StatusCodes.Status400BadRequest);
        problem.Extensions["errors"] = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(_ => "The value could not be read.").ToArray());
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}

// Runs the FluentValidation validator for each action argument (D-18). Failures are 422 validation_failed,
// with camelCase field names and messages in an errors object.
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        foreach (var argument in context.ActionArguments.Values.Where(value => value is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
                    .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
                context.Result = ((ControllerBase)context.Controller).Problem(ApiResults.ValidationFailed, errors);
                return;
            }
        }

        await next();
    }
}

public static class ApiSetup
{
    public static IServiceCollection AddMarketplaceControllers(this IServiceCollection services)
    {
        services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiResults.InvalidModelState);
        services.AddScoped<ValidationFilter>();
        return services;
    }

    // A handler result without a value (D-05).
    public static Result<Unit> Done() => Result.Success(Unit.Value);
}
