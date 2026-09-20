using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FreezeManager.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build a context without a host application.</summary>
public sealed class FreezeDbContextFactory : IDesignTimeDbContextFactory<FreezeDbContext>
{
    public FreezeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FreezeDbContext>()
            .UseSqlite("Data Source=freeze-design-time.db", FreezeDbOptions.Apply)
            .Options;

        return new FreezeDbContext(options);
    }
}
