using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Infrastructure.Persistence;

internal static class SqlServerErrors
{
    // 2601: unique index ihlali, 2627: unique veya primary key constraint ihlali.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}
