using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MicroService.Tests.Fakes;

public static class FakeDbContext
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
