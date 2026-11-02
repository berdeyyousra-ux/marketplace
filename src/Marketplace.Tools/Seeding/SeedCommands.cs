using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Marketplace.Tools.Seeding;

public static class SeedCommands
{
    // seed-load (section 13.4): the benchmark dataset.
    public static async Task<SeedDataset> LoadAsync(SeedOptions options, string connectionString, TextWriter log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var watch = Stopwatch.StartNew();
        var data = new SeedGenerator(options).Generate();
        await log.WriteLineAsync($"generated {data.Orders.Length:N0} orders and {data.Items.Length:N0} items in {watch.Elapsed.TotalSeconds:0.0} s");

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new SeedWriter(dataSource, log).WriteAsync(data, HashPassword(), cancellationToken);
        await log.WriteLineAsync($"done in {watch.Elapsed.TotalSeconds:0.0} s; users sign in with '{SeedOptions.Password}'");
        return data;
    }

    // seed-demo: a small browsable dataset whose product files exist in blob storage, so downloads work.
    public static async Task<SeedDataset> DemoAsync(
        SeedOptions options, string connectionString, string blobConnectionString, TextWriter log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var data = new SeedGenerator(options).Generate();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < data.Products.Length; i++)
        {
            ref var product = ref data.Products[i];
            if (product.FileKey is null)
            {
                continue;
            }

            var content = Encoding.UTF8.GetBytes($"{product.Title}\n\n{product.Description}\n\nThis is a demo file created by Marketplace.Tools seed-demo.\n");
            product.FileName = Path.ChangeExtension(product.FileName, "txt");
            product.FileContentType = "text/plain";
            product.FileSize = content.Length;
            product.FileSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
            files[product.FileKey] = content;
        }

        await using (var dataSource = NpgsqlDataSource.Create(connectionString))
        {
            await new SeedWriter(dataSource, log).WriteAsync(data, HashPassword(), cancellationToken);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:BlobStorage"] = blobConnectionString })
            .Build();
        var storage = new AzureBlobFileStorage(configuration, Microsoft.Extensions.Options.Options.Create(new StorageOptions()));
        foreach (var (key, content) in files)
        {
            using var stream = new MemoryStream(content);
            await storage.UploadAsync(key, stream, "text/plain", cancellationToken);
        }

        await log.WriteLineAsync($"uploaded {files.Count} product files");
        await log.WriteLineAsync(
            $"sign in as {data.Admin.Email}, {data.SellerUsers[0].Email} or {data.Buyers[0].Email} with the password '{SeedOptions.Password}'");
        return data;
    }

    // One hash for every seeded user: hashing 20,000 passwords at 210,000 iterations would take minutes.
    private static string HashPassword()
    {
        var hasher = new PasswordHasher<AppUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
        return hasher.HashPassword(new AppUser(), SeedOptions.Password);
    }
}
