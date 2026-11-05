using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Configuration;

public static partial class StartupValidation
{
    // Section 16: runs every ValidateOnStart rule now. Failures are logged at Critical (Serilog's Fatal) and rethrown,
    // so the process exits non-zero with the reason in the logs.
    public static void ValidateConfigurationOrLogFatal(this IServiceProvider services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            services.GetService<IStartupValidator>()?.Validate();
        }
        catch (Exception exception) when (exception is OptionsValidationException or AggregateException)
        {
            // Both messages list every failure: OptionsValidationException joins them, AggregateException appends each.
            LogInvalid(logger, exception, exception.Message);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Startup configuration is invalid: {Failures}")]
    private static partial void LogInvalid(ILogger logger, Exception exception, string failures);
}
