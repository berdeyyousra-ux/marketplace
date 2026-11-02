using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Storage;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// T-085: cover upload, its public URL on every product shape, and the separation of public covers from private files.
public sealed class CoverImageTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    // A structurally valid JPEG with an EXIF segment carrying a location, as phone photos do.
    public static byte[] JpegWithExif() =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8.ToArray(), 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xE1, 0x00, 0x18, .. "Exif\0\0"u8.ToArray(), .. "GPS 41.38N 2.17E"u8.ToArray(),
        0xFF, 0xDB, 0x00, 0x05, 0x00, 0x01, 0x02,
        0xFF, 0xDA, 0x00, 0x04, 0x01, 0x02, 0x11, 0x22, 0x33, 0xFF, 0xD9,
    ];

    [Fact]
    public async Task Upload_StripsExifAndTheCoverAppearsOnEveryProductShape()
    {
        using var seller = await AsSellerAsync("cover-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);

        var upload = await UploadAsync(seller, productId, JpegWithExif(), "cover.jpg");

        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var coverUrl = (await upload.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("coverImageUrl").GetString()!;
        var detail = await AsAnonymous().GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}", CancellationToken);
        var list = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products", CancellationToken);
        var mine = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/products", CancellationToken);
        detail.GetProperty("coverImageUrl").GetString().ShouldBe(coverUrl);
        list.GetProperty("items")[0].GetProperty("coverImageUrl").GetString().ShouldBe(coverUrl);
        mine.GetProperty("items")[0].GetProperty("coverImageUrl").GetString().ShouldBe(coverUrl);

        using var buyer = await AsBuyerAsync("cover-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        order.GetProperty("items")[0].GetProperty("coverImageUrl").GetString().ShouldBe(coverUrl);
        await PayOrderAsync(buyer, order.GetProperty("id").GetGuid());
        var library = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/library", CancellationToken);
        library.GetProperty("items")[0].GetProperty("coverImageUrl").GetString().ShouldBe(coverUrl);

        // Anyone can fetch it, and what they get has no location in it.
        using var anonymous = new HttpClient();
        var fetched = await anonymous.GetAsync(new Uri(coverUrl), CancellationToken);
        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        var served = await fetched.Content.ReadAsByteArrayAsync(CancellationToken);
        served.AsSpan().IndexOf("Exif"u8).ShouldBe(-1);
        served.AsSpan().IndexOf("GPS"u8).ShouldBe(-1);
        served.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }).ShouldBeTrue();
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "product.cover_updated", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task CoversAndDownloadableFiles_LiveInSeparateContainersWithOppositeAccess()
    {
        CatchUpWithRealTime();
        var options = Factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        options.CoverContainerName.ShouldNotBe(options.ContainerName);

        using var seller = await AsSellerAsync("separate-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var coverUrl = new Uri((await (await UploadAsync(seller, productId, JpegWithExif(), "cover.jpg")).Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .GetProperty("coverImageUrl").GetString()!);
        var fileKey = await WithDbAsync(db => db.Products.Where(product => product.Id == productId).Select(product => product.File!.Key).SingleAsync(CancellationToken));

        var service = new BlobServiceClient(Factory.BlobConnectionString);
        var files = service.GetBlobContainerClient(options.ContainerName);
        var covers = service.GetBlobContainerClient(options.CoverContainerName);

        // Access levels: the file container allows no anonymous access; the cover container allows anonymous blob reads.
        (await files.GetAccessPolicyAsync(cancellationToken: CancellationToken)).Value.BlobPublicAccess.ShouldBe(PublicAccessType.None);
        (await covers.GetAccessPolicyAsync(cancellationToken: CancellationToken)).Value.BlobPublicAccess.ShouldBe(PublicAccessType.Blob);

        // Each kind of blob is only in its own container.
        coverUrl.AbsolutePath.ShouldContain($"/{options.CoverContainerName}/covers/{productId}/");
        (await files.GetBlobsAsync(BlobTraits.None, BlobStates.None, "covers/", CancellationToken).ToListAsync(CancellationToken)).ShouldBeEmpty();
        (await covers.GetBlobsAsync(BlobTraits.None, BlobStates.None, "products/", CancellationToken).ToListAsync(CancellationToken)).ShouldBeEmpty();
        (await files.GetBlobClient(fileKey).ExistsAsync(CancellationToken)).Value.ShouldBeTrue();

        // Without a signed URL the downloadable file is unreachable; the cover is reachable by anyone.
        using var anonymous = new HttpClient();
        (await anonymous.GetAsync(files.GetBlobClient(fileKey).Uri, CancellationToken)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(coverUrl, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the storage refuses to be configured onto one container.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:BlobStorage"] = Factory.BlobConnectionString }).Build();
        Should.Throw<InvalidOperationException>(() => new AzureBlobPublicImageStorage(
            configuration, Options.Create(new StorageOptions { ContainerName = "same", CoverContainerName = "same" })));
    }

    [Fact]
    public async Task Replace_ServesTheNewCoverAndDeletesTheOldBlob()
    {
        using var seller = await AsSellerAsync("replace-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var first = await CoverUrlAsync(await UploadAsync(seller, productId, JpegWithExif(), "first.jpg"));
        Time.Advance(TimeSpan.FromSeconds(5));

        var second = await CoverUrlAsync(await UploadAsync(seller, productId, JpegWithExif(), "second.jpg"));
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken);

        second.ShouldNotBe(first);
        using var anonymous = new HttpClient();
        (await anonymous.GetAsync(first, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync(second, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AsAnonymous().GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}", CancellationToken)).GetProperty("coverImageUrl").GetString()
            .ShouldBe(second.ToString());
    }

    [Fact]
    public async Task Upload_RefusesTheWrongCallerTypeSizeAndState()
    {
        using var seller = await AsSellerAsync("rules-cover-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var otherSeller = await AsSellerAsync("rules-cover-other@example.test");
        using var buyer = await AsBuyerAsync("rules-cover-buyer@example.test");

        (await UploadAsync(otherSeller, productId, JpegWithExif(), "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await UploadAsync(buyer, productId, JpegWithExif(), "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(seller, productId, JpegWithExif(), "cover.gif")).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(seller, productId, "%PDF-1.7 not an image"u8.ToArray(), "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        var mismatch = await UploadAsync(seller, productId, JpegWithExif(), "cover.png");
        mismatch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await mismatch.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("file_content_mismatch");
        (await UploadAsync(seller, productId, [.. JpegWithExif()[..30]], "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var oversized = new byte[(5 * 1024 * 1024) + 10];
        JpegWithExif().CopyTo(oversized, 0);
        (await UploadAsync(seller, productId, oversized, "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        using var admin = await AsAdminAsync();
        (await admin.PostAsJsonAsync($"/api/v1/admin/products/{productId}/remove", new { reason = "Removed for the test." }, CancellationToken)).EnsureSuccessStatusCode();
        (await UploadAsync(seller, productId, JpegWithExif(), "cover.jpg")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.Products.Select(product => product.CoverImageKey).SingleAsync(CancellationToken))).ShouldBeNull();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid productId, byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PutAsync($"/api/v1/products/{productId}/cover", form, CancellationToken);
    }

    private static async Task<Uri> CoverUrlAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return new Uri((await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("coverImageUrl").GetString()!);
    }
}
