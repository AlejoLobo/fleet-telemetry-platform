using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetTelemetry.Infrastructure.Resilience;

// Clasifica fallos de base de datos como transitorios o permanentes (SQLSTATE / Npgsql).
public static class DatabaseTransientFailureClassifier
{
    private static readonly HashSet<string> TransientSqlStates = new(StringComparer.Ordinal)
    {
        "40001",
        "40P01",
        "53300",
        "57P03",
        "08000",
        "08001",
        "08003",
        "08004",
        "08006",
        "08007",
        "08P01"
    };

    private static readonly HashSet<string> PermanentSqlStates = new(StringComparer.Ordinal)
    {
        "23505",
        "23503",
        "23502",
        "23514",
        "22001",
        "22P02",
        "42P01",
        "42703"
    };

    public static bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
                return true;

            if (current is PostgresException postgres)
            {
                if (PermanentSqlStates.Contains(postgres.SqlState))
                    return false;
                if (TransientSqlStates.Contains(postgres.SqlState) || postgres.IsTransient)
                    return true;
                continue;
            }

            if (current is NpgsqlException npgsql)
            {
                if (!string.IsNullOrEmpty(npgsql.SqlState))
                {
                    if (PermanentSqlStates.Contains(npgsql.SqlState))
                        return false;
                    if (TransientSqlStates.Contains(npgsql.SqlState))
                        return true;
                }

                if (npgsql.IsTransient)
                    return true;
            }
        }

        // DbUpdateException u otros sin causa transitoria conocida → no reintentar.
        return false;
    }

    public static bool IsPermanentDatabaseFailure(Exception exception) =>
        exception is DbUpdateException || ContainsPermanentSqlState(exception);

    private static bool ContainsPermanentSqlState(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var sqlState = current switch
            {
                PostgresException p => p.SqlState,
                NpgsqlException n => n.SqlState,
                _ => null
            };

            if (!string.IsNullOrEmpty(sqlState) && PermanentSqlStates.Contains(sqlState))
                return true;
        }

        return false;
    }
}
