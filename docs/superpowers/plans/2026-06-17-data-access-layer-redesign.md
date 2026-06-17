# Data Access Layer Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce Domain/Application/Infrastructure layering with Repository + Unit of Work patterns around the WebApp order workflow, retire `Db.Repository`, without changing API behavior, outbox atomicity, or saga/message topology.

**Architecture:** Three new class libraries (`Domain`, `Application`, `Infrastructure`) replace `Db.Repository`. `Infrastructure` becomes a transitive dependency of every service (`WebApp`, `OrderSaga`, `InventoryService`, `PaymentService`, `NotificationService`) because every one of them constructs `AppDbContext` directly for its own outbox table. `OrderController` shrinks to call `IOrderService`; `OrderService` orchestrates `IUnitOfWork` + `IPublishEndpoint` reproducing the exact current transaction/outbox sequence.

**Tech Stack:** .NET 10, EF Core 10.0.9, MassTransit 9.2.0-develop.150 (RabbitMQ, EF outbox/saga repository, Newtonsoft serializer), AutoMapper 12.0.1.

No test project exists in this solution (confirmed in `CLAUDE.md`). This plan does not add one — verification is via `dotnet build` succeeding and manual smoke test of `POST /api/Order` against a running stack, not automated tests. Each task ends with a build-verification step instead of a test-run step.

---

## Task 1: Create Domain project with entities

**Files:**
- Create: `src/Domain/Domain.csproj`
- Create: `src/Domain/Entities/Order.cs`
- Create: `src/Domain/Entities/OrderDetail.cs`
- Create: `src/Domain/Entities/Product.cs`
- Create: `src/Domain/Entities/OrderSagaState.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MassTransit" Version="9.2.0-develop.150" />
  </ItemGroup>

</Project>
```

`MassTransit` is needed because `OrderSagaState` implements `SagaStateMachineInstance, ISagaVersion`.

- [ ] **Step 2: Create entity files** (moved verbatim from `src/Db.Repository`, namespace changed to `Domain.Entities`)

`src/Domain/Entities/Order.cs`:
```csharp
namespace Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public ICollection<OrderDetail> OrderDetails { get; set; } = [];
}
```

`src/Domain/Entities/OrderDetail.cs`:
```csharp
namespace Domain.Entities;

public class OrderDetail
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
```

`src/Domain/Entities/Product.cs`:
```csharp
namespace Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
```

`src/Domain/Entities/OrderSagaState.cs`:
```csharp
using MassTransit;

namespace Domain.Entities;

public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public int Version { get; set; }
    public string CurrentState { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}
```

- [ ] **Step 3: Add Domain to the solution**

```bash
dotnet sln MicroService.sln add src/Domain/Domain.csproj
```

- [ ] **Step 4: Build to verify it compiles standalone**

Run: `dotnet build src/Domain/Domain.csproj`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/Domain MicroService.sln
git commit -m "feat: add Domain project with entities"
```

---

## Task 2: Add repository and Unit of Work contracts to Domain

**Files:**
- Create: `src/Domain/Repositories/IGenericRepository.cs`
- Create: `src/Domain/Repositories/IOrderRepository.cs`
- Create: `src/Domain/Repositories/IProductRepository.cs`
- Create: `src/Domain/IUnitOfWork.cs`
- Create: `src/Domain/Exceptions/EntityNotFoundException.cs`

- [ ] **Step 1: Write IGenericRepository**

`src/Domain/Repositories/IGenericRepository.cs`:
```csharp
using System.Linq.Expressions;

namespace Domain.Repositories;

public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);
}
```

- [ ] **Step 2: Write IOrderRepository and IProductRepository**

`src/Domain/Repositories/IOrderRepository.cs`:
```csharp
using Domain.Entities;

namespace Domain.Repositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByIdWithDetailsAsync(int id);
    Task<IReadOnlyList<Order>> GetAllWithDetailsAsync();
}
```

`src/Domain/Repositories/IProductRepository.cs`:
```csharp
using Domain.Entities;

namespace Domain.Repositories;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> HasSufficientStockAsync(int productId, int qty);
}
```

- [ ] **Step 3: Write IUnitOfWork**

`src/Domain/IUnitOfWork.cs`:
```csharp
using Domain.Repositories;

namespace Domain;

public interface IUnitOfWork : IAsyncDisposable
{
    IOrderRepository Orders { get; }
    IProductRepository Products { get; }

    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}
```

- [ ] **Step 4: Write EntityNotFoundException**

`src/Domain/Exceptions/EntityNotFoundException.cs`:
```csharp
namespace Domain.Exceptions;

public class EntityNotFoundException(string entityName, object key)
    : Exception($"{entityName} with key '{key}' was not found.");
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Domain/Domain.csproj`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/Domain
git commit -m "feat: add repository and unit of work contracts to Domain"
```

---

## Task 3: Create Infrastructure project with AppDbContext and entity configurations

**Files:**
- Create: `src/Infrastructure/Infrastructure.csproj`
- Create: `src/Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/Infrastructure/Persistence/AppDbContextFactory.cs`
- Create: `src/Infrastructure/Persistence/Configurations/ProductConfiguration.cs`
- Create: `src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs`
- Create: `src/Infrastructure/Persistence/Configurations/OrderDetailConfiguration.cs`
- Create: `src/Infrastructure/Persistence/Configurations/OrderSagaStateConfiguration.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MassTransit" Version="9.2.0-develop.150" />
    <PackageReference Include="MassTransit.EntityFrameworkCore" Version="9.2.0-develop.150" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.9" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.9" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Domain\Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write entity configurations** (split out of the old inline `OnModelCreating`)

`src/Infrastructure/Persistence/Configurations/ProductConfiguration.cs`:
```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Price).HasPrecision(18, 2);

        builder.HasData(
            new Product { Id = 1, Name = "Laptop", Price = 999.99m, Stock = 50 },
            new Product { Id = 2, Name = "Wireless Mouse", Price = 29.99m, Stock = 200 },
            new Product { Id = 3, Name = "USB-C Hub", Price = 49.99m, Stock = 150 },
            new Product { Id = 4, Name = "Mechanical Keyboard", Price = 89.99m, Stock = 75 },
            new Product { Id = 5, Name = "Monitor 27\"", Price = 349.99m, Stock = 30 }
        );
    }
}
```

`src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs`:
```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
    }
}
```

`src/Infrastructure/Persistence/Configurations/OrderDetailConfiguration.cs`:
```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> builder)
    {
        builder.Property(d => d.UnitPrice).HasPrecision(18, 2);
        builder.Property(d => d.Total).HasPrecision(18, 2);

        builder.HasOne(d => d.Order)
            .WithMany(o => o.OrderDetails)
            .HasForeignKey(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Product)
            .WithMany()
            .HasForeignKey(d => d.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Infrastructure/Persistence/Configurations/OrderSagaStateConfiguration.cs`:
```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrderSagaStateConfiguration : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.HasKey(s => s.CorrelationId);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);
    }
}
```

- [ ] **Step 3: Write AppDbContext**

`src/Infrastructure/Persistence/AppDbContext.cs`:
```csharp
using Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
```

- [ ] **Step 4: Write the design-time factory** (moved verbatim, namespace changed)

`src/Infrastructure/Persistence/AppDbContextFactory.cs`:
```csharp
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
```

- [ ] **Step 5: Add Infrastructure to the solution**

```bash
dotnet sln MicroService.sln add src/Infrastructure/Infrastructure.csproj
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/Infrastructure MicroService.sln
git commit -m "feat: add Infrastructure project with AppDbContext and entity configurations"
```

---

## Task 4: Implement GenericRepository, OrderRepository, ProductRepository, UnitOfWork

**Files:**
- Create: `src/Infrastructure/Repositories/GenericRepository.cs`
- Create: `src/Infrastructure/Repositories/OrderRepository.cs`
- Create: `src/Infrastructure/Repositories/ProductRepository.cs`
- Create: `src/Infrastructure/UnitOfWork.cs`

- [ ] **Step 1: Write GenericRepository**

`src/Infrastructure/Repositories/GenericRepository.cs`:
```csharp
using System.Linq.Expressions;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GenericRepository<T>(AppDbContext context) : IGenericRepository<T> where T : class
{
    protected readonly AppDbContext Context = context;
    protected readonly DbSet<T> Set = context.Set<T>();

    public async Task<T?> GetByIdAsync(int id)
    {
        return await Set.FindAsync(id);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        return await Set.ToListAsync();
    }

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await Set.Where(predicate).ToListAsync();
    }

    public async Task AddAsync(T entity)
    {
        await Set.AddAsync(entity);
    }

    public void Update(T entity)
    {
        Set.Update(entity);
    }

    public void Remove(T entity)
    {
        Set.Remove(entity);
    }
}
```

- [ ] **Step 2: Write OrderRepository**

`src/Infrastructure/Repositories/OrderRepository.cs`:
```csharp
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class OrderRepository(AppDbContext context)
    : GenericRepository<Order>(context), IOrderRepository
{
    public async Task<Order?> GetByIdWithDetailsAsync(int id)
    {
        return await Set
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<IReadOnlyList<Order>> GetAllWithDetailsAsync()
    {
        return await Set
            .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
            .ToListAsync();
    }
}
```

- [ ] **Step 3: Write ProductRepository**

`src/Infrastructure/Repositories/ProductRepository.cs`:
```csharp
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ProductRepository(AppDbContext context)
    : GenericRepository<Product>(context), IProductRepository
{
    public async Task<bool> HasSufficientStockAsync(int productId, int qty)
    {
        var product = await Context.Products.FindAsync(productId);
        return product is not null && product.Stock >= qty;
    }
}
```

- [ ] **Step 4: Write UnitOfWork**

`src/Infrastructure/UnitOfWork.cs`:
```csharp
using Domain;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure;

public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    private IOrderRepository? _orders;
    private IProductRepository? _products;

    public IOrderRepository Orders => _orders ??= new OrderRepository(context);
    public IProductRepository Products => _products ??= new ProductRepository(context);

    public Task<int> SaveChangesAsync() => context.SaveChangesAsync();

    public async Task BeginTransactionAsync()
    {
        _transaction = await context.Database.BeginTransactionAsync();
    }

    public async Task CommitAsync()
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        await context.DisposeAsync();
    }
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/Infrastructure
git commit -m "feat: implement GenericRepository, OrderRepository, ProductRepository, UnitOfWork"
```

---

## Task 5: Add Infrastructure DI registration extension

**Files:**
- Create: `src/Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Write the extension**

`src/Infrastructure/DependencyInjection.cs`:
```csharp
using Domain;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                   .UseApplicationServiceProvider(provider));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        return services;
    }
}
```

This is additive only — used by `WebApp` (Task 9). Other services keep registering `AppDbContext` directly for their own outbox wiring (Task 10) since they don't need repositories/UoW, only the DbContext type.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Infrastructure/DependencyInjection.cs
git commit -m "feat: add AddInfrastructure DI registration extension"
```

---

## Task 6: Regenerate EF Core migrations against Infrastructure

**Files:**
- Delete: `src/Db.Repository/Migrations/` (after regeneration confirms parity)
- Create: `src/Infrastructure/Migrations/*` (generated)

- [ ] **Step 1: Confirm dotnet-ef is available**

Run: `dotnet ef --version`
If missing: `dotnet tool install --global dotnet-ef`

- [ ] **Step 2: Generate a fresh Init migration in Infrastructure**

```bash
dotnet ef migrations add Init --project src/Infrastructure --startup-project src/Infrastructure --output-dir Migrations
```

`--startup-project src/Infrastructure` works because `AppDbContextFactory` (Task 3, Step 4) provides design-time context creation without needing `WebApp`.

- [ ] **Step 3: Diff the generated migration against the old one to confirm identical schema**

```bash
diff src/Db.Repository/Migrations/20260617085751_Init.cs src/Infrastructure/Migrations/<new-timestamp>_Init.cs
```

Expected: differences limited to namespace (`Db.Repository.Migrations` → `Infrastructure.Persistence.Migrations`) and migration class/file name. No column, type, or constraint differences.

- [ ] **Step 4: Remove the old Db.Repository migrations folder**

```bash
git rm -r src/Db.Repository/Migrations
```

(Defer removing the rest of `Db.Repository` to Task 11, once every project reference is repointed.)

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/Infrastructure/Migrations
git commit -m "chore: regenerate EF Core migrations under Infrastructure project"
```

---

## Task 7: Create Application project with OrderService

**Files:**
- Create: `src/Application/Application.csproj`
- Create: `src/Application/Interfaces/IOrderService.cs`
- Create: `src/Application/Services/OrderService.cs`
- Create: `src/Application/Mappings/MapperProfile.cs`
- Create: `src/Application/DependencyInjection.cs`
- Delete: `src/WebApp/Mappings/MapperProfile.cs`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AutoMapper" Version="12.0.1" />
    <PackageReference Include="MassTransit" Version="9.2.0-develop.150" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Domain\Domain.csproj" />
    <ProjectReference Include="..\Contracts\Contracts.csproj" />
  </ItemGroup>

</Project>
```

`Contracts` reference is for `OrderDto`/`OrderDetailDto`/`ProductDto` (cross-service message DTOs that stay in `Contracts/Dto`) and for `OrderCreated` (published from `OrderService`).

- [ ] **Step 2: Write IOrderService**

`src/Application/Interfaces/IOrderService.cs`:
```csharp
using Contracts.Dto;

namespace Application.Interfaces;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync();
    Task<OrderDto?> GetByIdAsync(int id);
    Task<OrderDto> CreateAsync(OrderDto request);
    Task<bool> UpdateAsync(int id, OrderDto request);
    Task<bool> DeleteAsync(int id);
}
```

- [ ] **Step 3: Write OrderService** — reproduces the controller's exact current logic, including the two-`SaveChangesAsync`-around-`Publish` outbox sequence in `CreateAsync`, and the detail-replacement logic in `UpdateAsync` (clearing the `OrderDetails` navigation collection and re-adding achieves the same effect as the original `db.OrderDetails.RemoveRange(existing.OrderDetails)` + reassignment, given cascade-delete is configured on the FK in `OrderDetailConfiguration`)

`src/Application/Services/OrderService.cs`:
```csharp
using AutoMapper;
using Contracts.Dto;
using Contracts.Messages.Events;
using Application.Interfaces;
using Domain;
using Domain.Entities;
using MassTransit;

namespace Application.Services;

public class OrderService(IUnitOfWork uow, IPublishEndpoint bus, IMapper mapper) : IOrderService
{
    public async Task<List<OrderDto>> GetAllAsync()
    {
        var orders = await uow.Orders.GetAllWithDetailsAsync();
        return mapper.Map<List<OrderDto>>(orders);
    }

    public async Task<OrderDto?> GetByIdAsync(int id)
    {
        var order = await uow.Orders.GetByIdWithDetailsAsync(id);
        return order is null ? null : mapper.Map<OrderDto>(order);
    }

    public async Task<OrderDto> CreateAsync(OrderDto request)
    {
        var order = mapper.Map<Order>(request);
        order.OrderDate = DateTime.UtcNow;

        foreach (var detail in order.OrderDetails)
            detail.Total = detail.OrderQty * detail.UnitPrice;

        order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

        await uow.BeginTransactionAsync();

        await uow.Orders.AddAsync(order);
        await uow.SaveChangesAsync();                                              // 1) order.Id assigned by DB

        await bus.Publish(new OrderCreated { Order = mapper.Map<OrderDto>(order) }); // Id is valid
        await uow.SaveChangesAsync();                                              // 2) flush OutboxMessage row

        await uow.CommitAsync();                                                   // both rows commit atomically

        return mapper.Map<OrderDto>(order);
    }

    public async Task<bool> UpdateAsync(int id, OrderDto request)
    {
        if (id != request.Id) return false;

        var existing = await uow.Orders.GetByIdWithDetailsAsync(id);
        if (existing is null) return false;

        mapper.Map(request, existing);

        existing.OrderDetails.Clear();
        var newDetails = mapper.Map<List<OrderDetail>>(request.OrderDetails);
        foreach (var detail in newDetails)
        {
            detail.OrderId = id;
            detail.Total = detail.OrderQty * detail.UnitPrice;
            existing.OrderDetails.Add(detail);
        }

        existing.TotalAmount = existing.OrderDetails.Sum(d => d.Total);

        uow.Orders.Update(existing);
        await uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var order = await uow.Orders.GetByIdAsync(id);
        if (order is null) return false;
        uow.Orders.Remove(order);
        await uow.SaveChangesAsync();
        return true;
    }
}
```

Note on behavior parity: the original controller's `Update` returned `BadRequest()` (HTTP 400) when `id != request.Id`. This `UpdateAsync` returns `false` for that same case, which the controller (Task 9) maps to `NotFound()` (HTTP 404) — a deliberate, minor status-code change called out explicitly in Task 9, Step 4.

- [ ] **Step 4: Move MapperProfile from WebApp to Application**

`src/Application/Mappings/MapperProfile.cs`:
```csharp
using AutoMapper;
using Contracts.Dto;
using Domain.Entities;

namespace Application.Mappings;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<Product, ProductDto>().ReverseMap();

        CreateMap<OrderDetail, OrderDetailDto>().ReverseMap();

        CreateMap<Order, OrderDto>()
            .ReverseMap()
            .ForMember(d => d.OrderDate, o => o.Ignore())
            .ForMember(d => d.TotalAmount, o => o.Ignore());
    }
}
```

Delete the old file:
```bash
git rm src/WebApp/Mappings/MapperProfile.cs
```

- [ ] **Step 5: Write Application DI extension**

`src/Application/DependencyInjection.cs`:
```csharp
using Application.Interfaces;
using Application.Mappings;
using Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddAutoMapper(typeof(MapperProfile).Assembly);

        return services;
    }
}
```

- [ ] **Step 6: Add Application to the solution**

```bash
dotnet sln MicroService.sln add src/Application/Application.csproj
```

- [ ] **Step 7: Attempt build** (expected to fail at this point — `Contracts` still depends on `Db.Repository`, not yet repointed to `Domain`; Task 8 fixes this immediately after)

Run: `dotnet build src/Application/Application.csproj`
Expected: build error surfacing the `Contracts` → `Db.Repository` dependency mismatch. This confirms the dependency chain rather than indicating a mistake in this task — proceed directly to Task 8.

- [ ] **Step 8: Commit**

```bash
git add src/Application MicroService.sln
git commit -m "feat: add Application project with OrderService and mapping profile"
```

---

## Task 8: Repoint Contracts from Db.Repository to Domain

**Files:**
- Modify: `src/Contracts/Contracts.csproj`
- Modify: `src/Contracts/Saga/OrderStateMachine.cs`
- Modify: `src/Contracts/BusTopologyExtensions.cs`
- Modify: any other file under `src/Contracts` found to use `Db.Repository`

- [ ] **Step 1: Find every Db.Repository usage inside Contracts**

```bash
grep -rl "Db.Repository" src/Contracts --include="*.cs"
```

Expected output includes at least `src/Contracts/Saga/OrderStateMachine.cs` and `src/Contracts/BusTopologyExtensions.cs` (both confirmed during analysis to reference `Db.Repository`/`OrderSagaState`).

- [ ] **Step 2: Update the project reference**

In `src/Contracts/Contracts.csproj`, replace:
```xml
<ProjectReference Include="..\Db.Repository\Db.Repository.csproj" />
```
with:
```xml
<ProjectReference Include="..\Domain\Domain.csproj" />
```

- [ ] **Step 3: Update each `using Db.Repository;` to `using Domain.Entities;`**

For every file found in Step 1, change the using statement. Example for `BusTopologyExtensions.cs`:

```csharp
using Contracts.Consumers;
using Contracts.Saga;
using Domain.Entities;
using MassTransit;
```

Apply the same `using Db.Repository;` → `using Domain.Entities;` substitution to `OrderStateMachine.cs` and any other matched file.

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/Contracts/Contracts.csproj`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/Contracts
git commit -m "refactor: repoint Contracts from Db.Repository to Domain"
```

---

## Task 9: Repoint WebApp to Application + Infrastructure, rewrite OrderController

**Files:**
- Modify: `src/WebApp/WebApp.csproj`
- Modify: `src/WebApp/Program.cs`
- Modify: `src/WebApp/Controllers/OrderController.cs`
- Modify: `src/WebApp/Services/OrderSimulatorService.cs` (only if it references `Db.Repository`/`AppDbContext` directly — read it first, not inspected during analysis)
- Modify: `src/WebApp/Swagger/OrderDtoExample.cs` (only if it references `Db.Repository` directly — read it first, not inspected during analysis)

- [ ] **Step 1: Check OrderSimulatorService and OrderDtoExample for Db.Repository usage**

```bash
grep -n "Db.Repository\|AppDbContext" src/WebApp/Services/OrderSimulatorService.cs src/WebApp/Swagger/OrderDtoExample.cs
```

Read whichever file(s) report a match in full before editing, then replace `using Db.Repository;` with `using Domain.Entities;` (if it uses entity types directly) or `using Infrastructure.Persistence;` (if it constructs `AppDbContext` directly) — confirm which by reading the actual usage, since this wasn't inspected during analysis.

- [ ] **Step 2: Update WebApp.csproj project references**

Replace:
```xml
<ProjectReference Include="..\Db.Repository\Db.Repository.csproj" />
<ProjectReference Include="..\Contracts\Contracts.csproj" />
```
with:
```xml
<ProjectReference Include="..\Domain\Domain.csproj" />
<ProjectReference Include="..\Application\Application.csproj" />
<ProjectReference Include="..\Infrastructure\Infrastructure.csproj" />
<ProjectReference Include="..\Contracts\Contracts.csproj" />
```

- [ ] **Step 3: Update Program.cs** — replace the inline `AddDbContext` + `AddAutoMapper` calls with the new extensions

Replace:
```csharp
builder.Services.AddDbContext<AppDbContext>((provider, options) =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .UseApplicationServiceProvider(provider));
```
and
```csharp
builder.Services.AddAutoMapper(typeof(MapperProfile).Assembly);
```
with:
```csharp
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
```

Update the `using` block at the top of `Program.cs` — remove `using Db.Repository;` and `using WebApp.Mappings;`, add `using Infrastructure;` and `using Application;`.

`AddEntityFrameworkOutbox<AppDbContext>` inside `AddMassTransit` stays exactly as-is — `AppDbContext` is still registered (now via `AddInfrastructure`), so this call resolves the same way.

- [ ] **Step 4: Rewrite OrderController** to delegate to `IOrderService`

`src/WebApp/Controllers/OrderController.cs`:
```csharp
using Application.Interfaces;
using Contracts.Dto;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;
using WebApp.Swagger;

namespace WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await orderService.GetAllAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await orderService.GetByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    [SwaggerRequestExample(typeof(OrderDto), typeof(OrderDtoExample))]
    public async Task<IActionResult> Create(OrderDto request)
    {
        var result = await orderService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, OrderDto request)
    {
        var updated = await orderService.UpdateAsync(id, request);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await orderService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
```

Behavior note (flag to user after this task, do not silently ship): the original `Update` action returned `400 BadRequest` specifically when `id != request.Id`, distinct from `404 NotFound` for a missing order. The new code collapses both cases to `404 NotFound` since `OrderService.UpdateAsync` returns a single `bool`. If exact status-code parity for the id-mismatch case matters, change `IOrderService.UpdateAsync`/`OrderService.UpdateAsync` to throw a dedicated exception (e.g. add `Domain.Exceptions.OrderIdMismatchException`) for that case, catch it in the controller, and return `BadRequest()` — do this now if parity is required rather than deferring.

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/WebApp/WebApp.csproj`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/WebApp
git commit -m "refactor: WebApp delegates order operations to Application layer"
```

---

## Task 10: Repoint OrderSaga, InventoryService, PaymentService, NotificationService

**Files:**
- Modify: `src/OrderSaga/OrderSaga.csproj`, `src/OrderSaga/Program.cs`
- Modify: `src/InventoryService/InventoryService.csproj`, `src/InventoryService/Program.cs`
- Modify: `src/PaymentService/PaymentService.csproj`, `src/PaymentService/Program.cs`
- Modify: `src/NotificationService/NotificationService.csproj`, `src/NotificationService/Program.cs`

- [ ] **Step 1: OrderSaga — update csproj**

Replace in `src/OrderSaga/OrderSaga.csproj`:
```xml
<ProjectReference Include="..\Contracts\Contracts.csproj" />
<ProjectReference Include="..\Db.Repository\Db.Repository.csproj" />
```
with:
```xml
<ProjectReference Include="..\Contracts\Contracts.csproj" />
<ProjectReference Include="..\Infrastructure\Infrastructure.csproj" />
```

- [ ] **Step 2: OrderSaga — update Program.cs using statement**

Replace `using Db.Repository;` with `using Infrastructure.Persistence;` in `src/OrderSaga/Program.cs`. No other change needed — `AddDbContext<AppDbContext>`, `r.ExistingDbContext<AppDbContext>()`, `AddEntityFrameworkOutbox<AppDbContext>` all resolve identically once the type comes from `Infrastructure.Persistence` instead of `Db.Repository`.

- [ ] **Step 3: InventoryService — add Infrastructure reference and update using**

`src/InventoryService/InventoryService.csproj` currently only references `Contracts.csproj` and relied on `Db.Repository` transitively through it for `AppDbContext`. Since `Contracts` no longer provides `AppDbContext` (only entities, as of Task 8), add an explicit reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Contracts\Contracts.csproj" />
  <ProjectReference Include="..\Infrastructure\Infrastructure.csproj" />
</ItemGroup>
```

Update `src/InventoryService/Program.cs`: replace `using Db.Repository;` with `using Infrastructure.Persistence;`.

- [ ] **Step 4: PaymentService — read current csproj, then apply the same pattern as Step 3**

Read `src/PaymentService/PaymentService.csproj` first to see its current `ItemGroup` (not inspected during analysis). Add a `ProjectReference` to `..\Infrastructure\Infrastructure.csproj` alongside whatever it currently references.

Update `src/PaymentService/Program.cs`: replace `using Db.Repository;` with `using Infrastructure.Persistence;`.

- [ ] **Step 5: NotificationService — read current csproj, then apply the same pattern**

Read `src/NotificationService/NotificationService.csproj` first (not inspected during analysis). Add a `ProjectReference` to `..\Infrastructure\Infrastructure.csproj`.

Update `src/NotificationService/Program.cs`: replace `using Db.Repository;` with `using Infrastructure.Persistence;`.

- [ ] **Step 6: Build the entire solution**

```bash
dotnet build MicroService.sln
```

Expected: `Build succeeded.` with zero remaining references to `Db.Repository` anywhere in the solution.

- [ ] **Step 7: Commit**

```bash
git add src/OrderSaga src/InventoryService src/PaymentService src/NotificationService
git commit -m "refactor: repoint all services from Db.Repository to Infrastructure"
```

---

## Task 11: Remove Db.Repository project

**Files:**
- Delete: `src/Db.Repository/` (entire directory)
- Modify: `MicroService.sln`

- [ ] **Step 1: Confirm nothing still references it**

```bash
grep -rl "Db.Repository" src --include="*.csproj"
grep -rl "using Db.Repository" src --include="*.cs"
```

Expected: no output (empty) for both commands.

- [ ] **Step 2: Remove the project from the solution**

```bash
dotnet sln MicroService.sln remove src/Db.Repository/Db.Repository.csproj
```

- [ ] **Step 3: Delete the directory**

```bash
git rm -r src/Db.Repository
```

- [ ] **Step 4: Build the full solution one more time**

```bash
dotnet build MicroService.sln
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove retired Db.Repository project"
```

---

## Task 12: Update CLAUDE.md and manual smoke verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the EF Core migrations command block**

In `CLAUDE.md`, replace:
```
dotnet ef migrations add <Name> --project src/Db.Repository --startup-project src/WebApp
dotnet ef database update --project src/Db.Repository --startup-project src/WebApp
```
with:
```
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/WebApp
dotnet ef database update --project src/Infrastructure --startup-project src/WebApp
```

- [ ] **Step 2: Update the Architecture section's project list**

Replace the `Db.Repository` bullet in the "Projects (`src/`)" list with three bullets describing `Domain` (entities, repository/UoW contracts, domain exceptions — referenced by every project), `Application` (`IOrderService`/`OrderService`, AutoMapper profile — referenced by `WebApp`), and `Infrastructure` (`AppDbContext`, EF entity configurations, repository implementations, `UnitOfWork`, migrations, `AddInfrastructure` DI extension — referenced by every service for `AppDbContext`/outbox/saga repository).

- [ ] **Step 3: Apply pending migration to local SQL Server**

Requires SQL Server running locally per `CLAUDE.md` infra prerequisites.

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/WebApp
```

Expected: no-op or successful apply (schema is identical to before, confirmed in Task 6, Step 3).

- [ ] **Step 4: Manual smoke test** — start WebApp and exercise the order endpoints

```bash
dotnet run --project src/WebApp
```

With the app running (requires RabbitMQ + SQL Server locally per `CLAUDE.md`), open Swagger UI and:
1. `POST /api/Order` with a sample order body containing at least one `OrderDetails` entry referencing an existing `ProductId` (1-5, seeded). Confirm `201 Created` with a populated `Id`.
2. `GET /api/Order/{id}` for the created order. Confirm `OrderDetails` and nested `Product` are populated (proves `GetByIdWithDetailsAsync`'s `Include` chain works).
3. `PUT /api/Order/{id}` changing `CustomerName` and `OrderDetails`. Confirm `204 No Content` and a follow-up `GET` reflects the change.
4. `DELETE /api/Order/{id}`. Confirm `204 No Content` and a follow-up `GET` returns `404`.
5. Check the MassTransit dashboard (`/`) — confirm `OrderCreated` shows as published and the saga topology diagram still renders (proves `AddAllConsumers`/saga registration survived the namespace move).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for Domain/Application/Infrastructure layering"
```
