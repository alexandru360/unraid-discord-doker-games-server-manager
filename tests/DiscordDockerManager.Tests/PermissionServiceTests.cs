using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using DiscordDockerManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordDockerManager.Tests;

/// <summary>
///     Tests for <see cref="PermissionService" />.
/// </summary>
public class PermissionServiceTests : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly PermissionService _sut;

    public PermissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _dbFactory = new TestDbContextFactory(options);
        _sut = new PermissionService(_dbFactory, NullLogger<PermissionService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    // --- Helpers ---

    private async Task SeedContainerAsync(string name = "minecraft")
    {
        _db.DockerContainerConfigs.Add(new DockerContainerConfig
        {
            Name = name,
            ContainerId = $"{name}-server",
            Game = "Test",
            IsEnabled = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedAdminUserAsync(ulong userId = 111)
    {
        _db.UserPermissions.Add(new UserPermission
        {
            DiscordUserId = userId,
            Username = "admin",
            IsAdmin = true
        });
        await _db.SaveChangesAsync();
    }

    // --- Tests ---

    [Fact]
    public async Task HasPermission_Returns_False_For_Unknown_User()
    {
        await SeedContainerAsync();
        var result = await _sut.HasPermissionAsync(999UL, "minecraft");
        Assert.False(result);
    }

    [Fact]
    public async Task HasPermission_Returns_True_For_Admin()
    {
        await SeedContainerAsync();
        await SeedAdminUserAsync();

        var result = await _sut.HasPermissionAsync(111UL, "minecraft");
        Assert.True(result);
    }

    [Fact]
    public async Task GrantPermission_Allows_User_To_Manage_Container()
    {
        await SeedContainerAsync("valheim");

        var message = await _sut.GrantPermissionAsync(222UL, "testuser", "valheim");
        Assert.Contains("Granted", message);

        var allowed = await _sut.HasPermissionAsync(222UL, "valheim");
        Assert.True(allowed);
    }

    [Fact]
    public async Task GrantPermission_Returns_Error_For_Unknown_Container()
    {
        var message = await _sut.GrantPermissionAsync(333UL, "testuser", "nonexistent");
        Assert.Contains("not configured", message);
    }

    [Fact]
    public async Task GrantPermission_Is_Idempotent()
    {
        await SeedContainerAsync("terraria");
        await _sut.GrantPermissionAsync(444UL, "user", "terraria");
        var message = await _sut.GrantPermissionAsync(444UL, "user", "terraria");
        Assert.Contains("already has permission", message);
    }

    [Fact]
    public async Task RevokePermission_Removes_Access()
    {
        await SeedContainerAsync("rust");
        await _sut.GrantPermissionAsync(555UL, "rustplayer", "rust");

        Assert.True(await _sut.HasPermissionAsync(555UL, "rust"));

        var message = await _sut.RevokePermissionAsync(555UL, "rust");
        Assert.Contains("Revoked", message);

        Assert.False(await _sut.HasPermissionAsync(555UL, "rust"));
    }

    [Fact]
    public async Task RevokePermission_Returns_Error_When_No_Permission_Exists()
    {
        await SeedContainerAsync("ark");
        _db.UserPermissions.Add(new UserPermission { DiscordUserId = 666UL, Username = "noone" });
        await _db.SaveChangesAsync();

        var message = await _sut.RevokePermissionAsync(666UL, "ark");
        Assert.Contains("does not have permission", message);
    }

    [Fact]
    public async Task IsAdmin_Returns_False_For_Regular_User()
    {
        _db.UserPermissions.Add(new UserPermission { DiscordUserId = 777UL, Username = "regular" });
        await _db.SaveChangesAsync();
        Assert.False(await _sut.IsAdminAsync(777UL));
    }

    [Fact]
    public async Task IsAdmin_Returns_True_For_Admin_User()
    {
        await SeedAdminUserAsync(888UL);
        Assert.True(await _sut.IsAdminAsync(888UL));
    }
}

/// <summary>
///     Simple in-memory <see cref="IDbContextFactory{TContext}" /> for tests.
/// </summary>
file class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(_options);
    }
}