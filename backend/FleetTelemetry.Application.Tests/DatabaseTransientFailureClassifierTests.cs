using System.Reflection;
using FleetTelemetry.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetTelemetry.Application.Tests;

public class DatabaseTransientFailureClassifierTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [InlineData("53300")]
    [InlineData("57P03")]
    [InlineData("08000")]
    [InlineData("08001")]
    [InlineData("08003")]
    [InlineData("08004")]
    [InlineData("08006")]
    [InlineData("08007")]
    [InlineData("08P01")]
    public void Transient_sqlstates_are_transient(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);
        Assert.True(DatabaseTransientFailureClassifier.IsTransient(ex));
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("23503")]
    [InlineData("23502")]
    [InlineData("23514")]
    [InlineData("22001")]
    [InlineData("22P02")]
    [InlineData("42P01")]
    [InlineData("42703")]
    public void Permanent_sqlstates_are_not_transient(string sqlState)
    {
        var ex = CreatePostgresException(sqlState);
        Assert.False(DatabaseTransientFailureClassifier.IsTransient(ex));
    }

    [Fact]
    public void DbUpdateException_with_transient_inner_is_transient()
    {
        var inner = CreatePostgresException("40001");
        var ex = new DbUpdateException("update failed", inner);
        Assert.True(DatabaseTransientFailureClassifier.IsTransient(ex));
    }

    [Fact]
    public void DbUpdateException_with_permanent_inner_is_not_transient()
    {
        var inner = CreatePostgresException("23505");
        var ex = new DbUpdateException("update failed", inner);
        Assert.False(DatabaseTransientFailureClassifier.IsTransient(ex));
    }

    [Fact]
    public void TimeoutException_is_transient()
    {
        Assert.True(DatabaseTransientFailureClassifier.IsTransient(new TimeoutException("timeout")));
    }

    [Fact]
    public void InvalidOperationException_is_not_transient()
    {
        Assert.False(DatabaseTransientFailureClassifier.IsTransient(new InvalidOperationException("bug")));
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        var ctor = typeof(PostgresException).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(string), typeof(string), typeof(string)],
            modifiers: null);

        Assert.NotNull(ctor);
        return (PostgresException)ctor!.Invoke(["test", "ERROR", "ERROR", sqlState]);
    }
}
