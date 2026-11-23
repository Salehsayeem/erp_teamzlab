using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Api.Data;

// Design-time factory for `dotnet ef migrations add` — builds the model from the env-assembled
// Default connection string without ever opening a connection (migrations never target a server
// here; `database update` is explicitly not run against Default per the task brief).
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args)
    {
        EnvLoader.Load();
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(DbConfig.Assemble(key => Environment.GetEnvironmentVariable(key)))
            .Options;
        return new ErpDbContext(options);
    }
}
