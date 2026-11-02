using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Catalog;

public sealed class ProductTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _sellerId = Guid.CreateVersion7();

    public static TheoryData<ProductStatus, ProductStatus> AllTransitions()
    {
        var data = new TheoryData<ProductStatus, ProductStatus>();
        foreach (var from in Enum.GetValues<ProductStatus>())
        {
            foreach (var to in Enum.GetValues<ProductStatus>().Where(status => status != ProductStatus.Draft))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void Create_ValidDetails_IsDraft()
    {
        var result = Product.Create(_sellerId, 1, "Notion Budget Template", "A budget template.", Money.Eur(19.99m), _now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ProductStatus.Draft);
        result.Value.CreatedAt.ShouldBe(_now);
        result.Value.Id.Version.ShouldBe(7);
    }

    [Theory]
    [InlineData(0, "Valid title", "Valid description", "19.99")]
    [InlineData(1, "ab", "Valid description", "19.99")]
    [InlineData(1, "   ", "Valid description", "19.99")]
    [InlineData(1, "Valid title", "", "19.99")]
    [InlineData(1, "Valid title", "Valid description", "0.49")]
    [InlineData(1, "Valid title", "Valid description", "10000.01")]
    public void Create_InvalidDetails_ReturnsValidationFailed(int categoryId, string title, string description, string price)
    {
        var result = Product.Create(_sellerId, categoryId, title, description, Money.Eur(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)), _now);

        result.Error.Code.ShouldBe("validation_failed");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_LongestTitleAndDescription_Succeeds()
    {
        var result = Product.Create(_sellerId, 1, new string('t', 120), new string('d', 5000), Money.Eur(10000m), _now);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_TooLongTitle_ReturnsValidationFailed()
    {
        var result = Product.Create(_sellerId, 1, new string('t', 121), "Valid description", Money.Eur(1m), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Create_NonEuroPrice_ReturnsValidationFailed()
    {
        var result = Product.Create(_sellerId, 1, "Valid title", "Valid description", new Money(5m, "USD"), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Update_RemovedProduct_ReturnsInvalidStateTransition()
    {
        var product = InState(ProductStatus.Removed);

        var result = product.Update(1, "New title", "New description", Money.Eur(5m), _now);

        result.Error.Code.ShouldBe("invalid_state_transition");
    }

    [Fact]
    public void Update_PublishedProduct_ChangesDetails()
    {
        var product = InState(ProductStatus.Published);

        var result = product.Update(2, "New title", "New description", Money.Eur(5m), _now.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        product.Title.ShouldBe("New title");
        product.Price.ShouldBe(Money.Eur(5m));
        product.UpdatedAt.ShouldBe(_now.AddHours(1));
    }

    [Fact]
    public void Publish_WithoutFile_ReturnsProductIncomplete()
    {
        var product = Product.Create(_sellerId, 1, "Notion Budget Template", "A budget template.", Money.Eur(19.99m), _now).Value;

        var result = product.Publish(_now);

        result.Error.Code.ShouldBe("product_incomplete");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        product.Status.ShouldBe(ProductStatus.Draft);
    }

    [Fact]
    public void Publish_AfterUnpublish_KeepsFirstPublishedAt()
    {
        var product = InState(ProductStatus.Draft);
        product.AttachFile(ValidFile(), _now).IsSuccess.ShouldBeTrue();
        product.Publish(_now).IsSuccess.ShouldBeTrue();
        product.Unpublish(_now.AddDays(1)).IsSuccess.ShouldBeTrue();

        product.Publish(_now.AddDays(2)).IsSuccess.ShouldBeTrue();

        product.PublishedAt.ShouldBe(_now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Remove_BlankReason_ReturnsValidationFailed(string reason)
    {
        var product = InState(ProductStatus.Published);

        var result = product.Remove(reason, _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Remove_TooLongReason_ReturnsValidationFailed()
    {
        var product = InState(ProductStatus.Published);

        var result = product.Remove(new string('r', 501), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void AttachFile_RemovedProduct_ReturnsInvalidStateTransition()
    {
        var product = InState(ProductStatus.Removed);

        var result = product.AttachFile(ValidFile(), _now);

        result.Error.Code.ShouldBe("invalid_state_transition");
    }

    [Theory]
    [InlineData("", "file.pdf", 1, "application/pdf")]
    [InlineData("products/a/b", "", 1, "application/pdf")]
    [InlineData("products/a/b", "file.pdf", 0, "application/pdf")]
    [InlineData("products/a/b", "file.pdf", 1, "")]
    public void ProductFileCreate_InvalidMetadata_ReturnsValidationFailed(string key, string name, long size, string contentType)
    {
        var result = ProductFile.Create(key, name, size, contentType, new string('a', 64));

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void ProductFileCreate_InvalidSha256_ReturnsValidationFailed(string sha256)
    {
        var result = ProductFile.Create("products/a/b", "file.pdf", 1, "application/pdf", sha256);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void StateMachine_EveryTransition_MatchesSection63(ProductStatus from, ProductStatus to)
    {
        var product = InState(from);
        var legal = IsLegal(from, to);

        var result = to switch
        {
            ProductStatus.Published => product.Publish(_now),
            ProductStatus.Unpublished => product.Unpublish(_now),
            ProductStatus.Removed => product.Remove("Policy violation", _now),
            _ => throw new ArgumentOutOfRangeException(nameof(to)),
        };

        result.IsSuccess.ShouldBe(legal);
        if (legal)
        {
            product.Status.ShouldBe(to);
        }
        else
        {
            result.Error.Code.ShouldBe("invalid_state_transition");
            product.Status.ShouldBe(from);
        }
    }

    private static bool IsLegal(ProductStatus from, ProductStatus to) => (from, to) switch
    {
        (ProductStatus.Draft, ProductStatus.Published) => true,
        (ProductStatus.Published, ProductStatus.Unpublished) => true,
        (ProductStatus.Unpublished, ProductStatus.Published) => true,
        (not ProductStatus.Removed, ProductStatus.Removed) => true,
        _ => false,
    };

    private static ProductFile ValidFile() =>
        ProductFile.Create("products/a/b", "template.pdf", 1024, "application/pdf", new string('a', 64)).Value;

    private static Product InState(ProductStatus status)
    {
        var product = Product.Create(_sellerId, 1, "Notion Budget Template", "A budget template.", Money.Eur(19.99m), _now).Value;
        product.AttachFile(ValidFile(), _now).IsSuccess.ShouldBeTrue();

        Result result = status switch
        {
            ProductStatus.Draft => Result.Success(),
            ProductStatus.Published => product.Publish(_now),
            ProductStatus.Unpublished => product.Publish(_now).IsSuccess ? product.Unpublish(_now) : Result.Failure(Product.Incomplete),
            ProductStatus.Removed => product.Remove("Policy violation", _now),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        result.IsSuccess.ShouldBeTrue();
        return product;
    }
}
