using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed record ProductFile
{
    private ProductFile(string key, string name, long sizeBytes, string contentType, string sha256)
    {
        Key = key;
        Name = name;
        SizeBytes = sizeBytes;
        ContentType = contentType;
        Sha256 = sha256;
    }

    public string Key { get; }

    public string Name { get; }

    public long SizeBytes { get; }

    public string ContentType { get; }

    public string Sha256 { get; }

    // Column limits from the products table in section 7.2.
    public static Result<ProductFile> Create(string key, string name, long sizeBytes, string contentType, string sha256)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 300)
        {
            return DomainErrors.Validation("File key must be 1 to 300 characters.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return DomainErrors.Validation("File name must be 1 to 255 characters.");
        }

        if (sizeBytes <= 0)
        {
            return DomainErrors.Validation("File size must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 100)
        {
            return DomainErrors.Validation("Content type must be 1 to 100 characters.");
        }

        if (sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigitLower))
        {
            return DomainErrors.Validation("SHA-256 must be 64 lowercase hexadecimal characters.");
        }

        return new ProductFile(key, name, sizeBytes, contentType, sha256);
    }
}
