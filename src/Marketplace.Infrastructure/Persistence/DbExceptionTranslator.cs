using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marketplace.Infrastructure.Persistence;

public sealed partial class DbExceptionTranslator(ILogger<DbExceptionTranslator> logger) : IDbExceptionTranslator
{
    public Error? Translate(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbUpdateConcurrencyException)
        {
            return DatabaseErrors.ConcurrencyConflict;
        }

        if (exception.InnerException is not PostgresException postgres)
        {
            return null;
        }

        if (DatabaseErrors.IsBug(postgres.SqlState))
        {
            LogConstraintBug(logger, postgres.SqlState, postgres.ConstraintName, postgres.TableName);
        }

        return DatabaseErrors.FromPostgres(postgres.SqlState, postgres.ConstraintName);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Database rejected a write: SQLSTATE {SqlState} on constraint {ConstraintName} of table {TableName}")]
    private static partial void LogConstraintBug(ILogger logger, string sqlState, string? constraintName, string? tableName);
}
