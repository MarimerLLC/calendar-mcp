using Adjutant.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Adjutant.HttpServer.Identity;

/// <summary>
/// OpenIddict's applications (registered MCP clients), authorizations and tokens, in SQLite in the
/// data directory (<c>identity.db</c>).
///
/// SQLite is safe here because the server runs as a single process and the data directory is a
/// local disk or a block volume. It must not be on a network file share (NFS, SMB, Azure Files,
/// a Kubernetes ReadWriteMany volume): file locking there is unreliable.
///
/// The schema is created with <c>EnsureCreated</c>, not migrations. If a future OpenIddict version
/// ever changes it, deleting the file is safe: it only means connected apps sign in again.
/// </summary>
internal sealed class OAuthDbContext(DbContextOptions<OAuthDbContext> options) : DbContext(options)
{
    public const string FileName = "identity.db";

    public static string DatabasePath() =>
        Path.Combine(ConfigurationPaths.GetDataDirectory(), FileName);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
    }

    /// <summary>
    /// Creates the database on first start and switches it to write-ahead logging, so token
    /// lookups on MCP requests don't wait behind an authorization being written.
    /// </summary>
    public static void EnsureReady(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OAuthDbContext>();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
