using System.Text.Json;
using System.Text.Json.Serialization;
using FreezeManager.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace FreezeManager.Infrastructure.Persistence;

/// <summary>Loads the service catalogue from a JSON file, inserting or updating by key.</summary>
public static class ServiceCatalogSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> SeedFromFileAsync(
        FreezeDbContext db,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = File.OpenRead(path);

        var document = await JsonSerializer.DeserializeAsync<ServiceCatalogDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Service catalogue at '{path}' is empty.");

        var existing = await db.Services.ToDictionaryAsync(s => s.Key, cancellationToken);
        var written = 0;

        foreach (var item in document.Services ?? new List<ServiceCatalogItem>())
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Owner))
            {
                throw new InvalidDataException($"Service catalogue entry '{item.Key ?? "?"}' is missing a key, name or owner.");
            }

            if (existing.TryGetValue(item.Key, out var record))
            {
                record.Name = item.Name;
                record.Tier = item.Tier;
                record.Owner = item.Owner;
            }
            else
            {
                db.Services.Add(new ServiceRecord
                {
                    Key = item.Key,
                    Name = item.Name,
                    Tier = item.Tier,
                    Owner = item.Owner
                });
            }

            written++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return written;
    }

    public sealed class ServiceCatalogDocument
    {
        public string? Disclaimer { get; set; }

        public List<ServiceCatalogItem>? Services { get; set; }
    }

    public sealed class ServiceCatalogItem
    {
        public string? Key { get; set; }

        public string? Name { get; set; }

        public ServiceTier Tier { get; set; }

        public string? Owner { get; set; }
    }
}
