using System.Diagnostics.CodeAnalysis;

namespace Marketplace.Domain.Common;

[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Section 6.1 of the build spec names this type Error. The rule protects Visual Basic callers, and this codebase is C# only.")]
// Fields optionally names the inputs at fault, rendered as the problem's "errors" object (section 8.1).
public sealed record Error(string Code, string Message, ErrorType Type, IReadOnlyDictionary<string, string[]>? Fields = null);
