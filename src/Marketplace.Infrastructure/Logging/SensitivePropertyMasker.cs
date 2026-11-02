using Serilog.Core;
using Serilog.Events;

namespace Marketplace.Infrastructure.Logging;

// Section 11.3: log properties whose names contain password, token, secret, authorization, clientSecret
// or apiKey are masked, including inside destructured objects, dictionaries and sequences.
public sealed class SensitivePropertyMasker : ILogEventEnricher
{
    public const string Mask = "***";

    private static readonly string[] _sensitiveNames = ["password", "token", "secret", "authorization", "clientsecret", "apikey"];
    private static readonly ScalarValue _masked = new(Mask);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var masked = MaskValue(name, value);
            if (!ReferenceEquals(masked, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, masked));
            }
        }
    }

    public static bool IsSensitive(string name) =>
        _sensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

    private static LogEventPropertyValue MaskValue(string name, LogEventPropertyValue value)
    {
        if (IsSensitive(name))
        {
            return _masked;
        }

        return value switch
        {
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property => new LogEventProperty(property.Name, MaskValue(property.Name, property.Value))),
                structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(element =>
                KeyValuePair.Create(element.Key, MaskValue(element.Key.Value?.ToString() ?? string.Empty, element.Value)))),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(element => MaskValue(string.Empty, element))),
            _ => value,
        };
    }
}
