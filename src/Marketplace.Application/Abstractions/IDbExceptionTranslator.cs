using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Abstractions;

// Section 7.4: database errors are translated in one place.
public interface IDbExceptionTranslator
{
    // Returns the client-facing error for an expected failure, or null when the exception is a bug and should become a 500.
    Error? Translate(DbUpdateException exception);
}
