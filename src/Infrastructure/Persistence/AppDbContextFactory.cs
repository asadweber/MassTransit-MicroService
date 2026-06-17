using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=OrderDB;User Id=sa;Password=Asdf1234;" +
            "Connect Timeout=30;Min Pool Size=5;Max Pool Size=100;TrustServerCertificate=True;");

        return new AppDbContext(optionsBuilder.Options);
    }
}
