using Microsoft.Data.SqlClient;

namespace AotSample;

/// <summary>
/// Creates and seeds the sample database with plain SQL: EF Core migrations are not available under Native AOT, and
/// EF Core 8 cannot save changes there.
/// </summary>
internal static class SampleDatabase
{
    private const string DatabaseName = "KetPsiAotSample";

    private const string CreateDatabase = $"IF DB_ID('{DatabaseName}') IS NULL CREATE DATABASE [{DatabaseName}];";

    private const string ResetSchema = """
        IF SCHEMA_ID('shop') IS NULL EXEC('CREATE SCHEMA shop');
        IF OBJECT_ID('shop.Products') IS NOT NULL DROP TABLE shop.Products;
        CREATE TABLE shop.Products (
            Id int NOT NULL PRIMARY KEY,
            Name nvarchar(100) NOT NULL,
            Price decimal(10, 2) NOT NULL,
            Stock int NULL,
            IsActive bit NOT NULL,
            Category int NOT NULL,
            CreatedAt datetime2 NOT NULL,
            Revision int NOT NULL);
        INSERT INTO shop.Products (Id, Name, Price, Stock, IsActive, Category, CreatedAt, Revision) VALUES
            (1, N'Dune', 9.99, 12, 1, 0, '2024-01-01T08:00:00', 10),
            (2, N'Hades', 24.50, NULL, 1, 1, '2024-01-02T08:00:00', 20),
            (3, N'Abbey Road', 15.00, 3, 0, 2, '2024-01-03T08:00:00', 30),
            (4, N'Celeste', 19.99, 0, 1, 1, '2024-01-04T08:00:00', 40),
            (5, N'Neuromancer', 7.25, 40, 1, 0, '2024-01-05T08:00:00.1234567', 50),
            (6, N'Kind of Blue', 12.00, NULL, 1, 2, '2024-01-06T08:00:00', 60),
            (7, N'Outer Wilds', 22.00, 8, 0, 1, '2024-01-07T08:00:00', 70),
            (8, N'Foundation', 8.75, 15, 1, 0, '2024-01-08T08:00:00', 80);
        """;

    /// <summary>Recreates the sample table and returns the connection string of the sample database.</summary>
    public static async Task<string> ResetAsync(string serverConnectionString, CancellationToken cancellationToken)
    {
        await using (var server = new SqlConnection(serverConnectionString))
        {
            await server.OpenAsync(cancellationToken);
            await ExecuteAsync(server, CreateDatabase, cancellationToken);
        }

        var connectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = DatabaseName }.ConnectionString;
        await using var database = new SqlConnection(connectionString);
        await database.OpenAsync(cancellationToken);
        await ExecuteAsync(database, ResetSchema, cancellationToken);

        return connectionString;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
