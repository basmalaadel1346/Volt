using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AssessmentBL.Services;

/// <summary>
/// Recognises the SQL Server uniqueness errors the Assessment schema relies
/// on, so services can translate them into business conflicts instead of
/// letting a raw provider exception reach the client as a 500.
/// </summary>
internal static class DbUpdateExceptionExtensions
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && sql.Number is UniqueIndexViolation or UniqueConstraintViolation;

    /// <summary>
    /// True when the violated index/constraint is the named one. SQL Server
    /// puts the constraint name in the error text, which is the only place
    /// the provider exposes it.
    /// </summary>
    public static bool IsUniqueViolationOf(this DbUpdateException exception, string constraintName) =>
        exception.IsUniqueViolation()
        && exception.InnerException!.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
}
