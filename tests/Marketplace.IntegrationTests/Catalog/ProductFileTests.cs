using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Storage;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// EP-25, US-12 and the section 9.2 upload rules, against Azurite.
public sealed class ProductFileTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private static readonly byte[] _pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nA tiny test document.\n");
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    private AzureBlobFileStorage Storage => (AzureBlobFileStorage)Factory.Services.GetRequiredService<IFileStorage>();

    [Fact]
    public async Task US12_UploadPdf_StoresBlobAndRecordsMetadata()
    {
        using var seller = await AsSellerAsync("upload@example.test");
        var id = await CreateProductAsync(seller);

        var response = await UploadAsync(seller, id, "budget.pdf", _pdf);
        var file = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.GetProperty("name").GetString().ShouldBe("budget.pdf");
        file.GetProperty("sizeBytes").GetInt64().ShouldBe(_pdf.Length);
        file.GetProperty("contentType").GetString().ShouldBe("application/pdf");
        file.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_pdf)));
        var key = await FileKeyAsync(id);
        key.ShouldStartWith($"products/{id}/");
        (await Storage.ExistsAsync(key!, CancellationToken)).ShouldBeTrue();
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "product.file_uploaded", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US12_MagicByteMismatch_Returns422AndStoresNothing()
    {
        using var seller = await AsSellerAsync("mismatch@example.test");
        var id = await CreateProductAsync(seller);

        var response = await UploadAsync(seller, id, "not-really.pdf", _png);

        await ShouldHaveCodeAsync(response, HttpStatusCode.UnprocessableEntity, "file_content_mismatch");
        (await BlobCountAsync(id)).ShouldBe(0);
        (await FileKeyAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task US12_DisallowedExtension_Returns415()
    {
        using var seller = await AsSellerAsync("exe@example.test");
        var id = await CreateProductAsync(seller);

        await ShouldHaveCodeAsync(await UploadAsync(seller, id, "setup.exe", _pdf), HttpStatusCode.UnsupportedMediaType, "unsupported_file_type");
    }

    [Fact]
    public async Task US12_PathInFileName_IsStripped()
    {
        using var seller = await AsSellerAsync("path@example.test");
        var id = await CreateProductAsync(seller);

        var file = await (await UploadAsync(seller, id, "../../etc/report.pdf", _pdf)).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        file.GetProperty("name").GetString().ShouldBe("report.pdf");
    }

    [Fact]
    public async Task US12_OtherSeller_Gets404AndStoresNothing()
    {
        using var owner = await AsSellerAsync("file-owner@example.test");
        using var other = await AsSellerAsync("file-other@example.test", "Other Studio");
        var id = await CreateProductAsync(owner);

        (await UploadAsync(other, id, "budget.pdf", _pdf)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BlobCountAsync(id)).ShouldBe(0);
    }

    [Fact]
    public async Task US12_ReplacingFile_DeletesOldBlobThroughOutbox()
    {
        using var seller = await AsSellerAsync("replace@example.test");
        var id = await CreateProductAsync(seller);
        await UploadAsync(seller, id, "v1.pdf", _pdf);
        var firstKey = await FileKeyAsync(id);

        await UploadAsync(seller, id, "v2.pdf", _pdf);
        var types = await WithDbAsync(db => db.OutboxMessages.Select(message => message.Type).ToListAsync(CancellationToken));
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

        types.ShouldContain(OutboxMessageTypes.DeleteBlob);
        (await Storage.ExistsAsync(firstKey!, CancellationToken)).ShouldBeFalse();
        (await Storage.ExistsAsync((await FileKeyAsync(id))!, CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task US14_PublishAfterUpload_Succeeds()
    {
        using var seller = await AsSellerAsync("publish-upload@example.test");
        var id = await CreateProductAsync(seller);
        await UploadAsync(seller, id, "budget.pdf", _pdf);
        var etag = (await seller.GetAsync($"/api/v1/products/{id}", CancellationToken)).Headers.ETag!.Tag;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{id}/publish");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var published = await seller.SendAsync(request, CancellationToken);

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task US12_Exactly100Megabytes_IsAccepted()
    {
        using var seller = await AsSellerAsync("hundred@example.test");
        var id = await CreateProductAsync(seller);

        var response = await UploadStreamAsync(seller, id, "large.pdf", new GeneratedPdfStream(100L * 1024 * 1024));
        var file = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.GetProperty("sizeBytes").GetInt64().ShouldBe(100L * 1024 * 1024);
    }

    [Fact]
    public async Task US12_OverOneHundredMegabytes_Returns413AndStoresNothing()
    {
        using var seller = await AsSellerAsync("too-large@example.test");
        var id = await CreateProductAsync(seller);

        var response = await UploadStreamAsync(seller, id, "large.pdf", new GeneratedPdfStream((100L * 1024 * 1024) + 1));

        await ShouldHaveCodeAsync(response, HttpStatusCode.RequestEntityTooLarge, "file_too_large");
        (await BlobCountAsync(id)).ShouldBe(0);
    }

    private static async Task<string> CreateProductAsync(HttpClient seller)
    {
        var response = await seller.PostAsJsonAsync(
            "/api/v1/products",
            new { title = "Notion Budget Template", description = "A budget template.", price = new { amount = "19.99", currency = "EUR" }, categoryId = 2 },
            TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string id, string fileName, byte[] bytes) =>
        UploadStreamAsync(client, id, fileName, new MemoryStream(bytes));

    private static async Task<HttpResponseMessage> UploadStreamAsync(HttpClient client, string id, string fileName, Stream content)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/v1/products/{id}/file", form, TestContext.Current.CancellationToken);
    }

    private Task<string?> FileKeyAsync(string id) =>
        WithDbAsync(db => db.Products.Where(product => product.Id == Guid.Parse(id)).Select(product => product.File!.Key).SingleAsync(CancellationToken))!;

    private async Task<int> BlobCountAsync(string id)
    {
        if (!await Storage.Container.ExistsAsync(CancellationToken))
        {
            return 0;
        }

        var count = 0;
        await foreach (var _ in Storage.Container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, $"products/{id}/", CancellationToken))
        {
            count++;
        }

        return count;
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }

    // A PDF header followed by zeros, produced on the fly so large uploads never sit in memory.
    private sealed class GeneratedPdfStream(long length) : Stream
    {
        private static readonly byte[] _header = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var toWrite = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, toWrite);
            for (var i = 0; i < toWrite && _position + i < _header.Length; i++)
            {
                buffer[offset + i] = _header[_position + i];
            }

            _position += toWrite;
            return toWrite;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
