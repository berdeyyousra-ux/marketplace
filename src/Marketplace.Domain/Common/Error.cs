using System.Diagnostics.CodeAnalysis;

namespace Marketplace.Domain.Common;

[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Section 6.1 of the build spec names this type Error. The rule protects Visual Basic callers, and this codebase is C# only.")]
public sealed record Error(string Code, string Message, ErrorType Type);
