namespace Marketplace.Domain.Common;

public static class DomainErrors
{
    public static Error InvalidStateTransition(string entity, Enum from, Enum to) =>
        new("invalid_state_transition", $"{entity} cannot move from {from} to {to}.", ErrorType.Conflict);

    public static Error InvalidState(string message) =>
        new("invalid_state_transition", message, ErrorType.Conflict);

    public static Error Validation(string message) =>
        new("validation_failed", message, ErrorType.Validation);
}
