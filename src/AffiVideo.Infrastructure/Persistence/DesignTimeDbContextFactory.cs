using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AffiVideo.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model with no running database and no configuration.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AffiVideoDbContext>
{
    public AffiVideoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AffiVideoDbContext>().UseNpgsql().Options);
}
