using FreezeManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Tests.Support;

/// <summary>
/// An in-memory SQLite database that lives as long as its connection. Created through the real
/// migrations, so the migrations are under test too.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Options = new DbContextOptionsBuilder<FreezeDbContext>()
            .UseSqlite(_connection, FreezeDbOptions.Apply)
            .Options;

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public DbContextOptions<FreezeDbContext> Options { get; }

    public FreezeDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
