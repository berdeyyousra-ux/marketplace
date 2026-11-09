using Marketplace.Domain.Fulfillment;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Fulfillment;

public sealed class DownloadGrantTests
{
    private static readonly DateTimeOffset _paidAt = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AtPaymentTime_SetsSection62Limits()
    {
        var grant = NewGrant();

        grant.ExpiresAt.ShouldBe(_paidAt.AddDays(365));
        grant.MaxDownloads.ShouldBe(10);
        grant.DownloadCount.ShouldBe(0);
        grant.CanDownload(_paidAt).ShouldBeTrue();
    }

    [Fact]
    public void RecordDownload_Available_IncrementsCount()
    {
        var grant = NewGrant();

        grant.RecordDownload(_paidAt).IsSuccess.ShouldBeTrue();

        grant.DownloadCount.ShouldBe(1);
    }

    [Fact]
    public void RecordDownload_BeyondTenth_StaysAvailable()
    {
        var grant = NewGrant();
        for (var i = 0; i < 25; i++)
        {
            grant.RecordDownload(_paidAt).IsSuccess.ShouldBeTrue();
        }

        grant.CanDownload(_paidAt).ShouldBeTrue();
        grant.DownloadCount.ShouldBe(25);
    }

    [Fact]
    public void CanDownload_AtExpiry_IsExpired()
    {
        var grant = NewGrant();

        grant.CanDownload(grant.ExpiresAt.AddTicks(-1)).ShouldBeTrue();
        grant.GetUnavailableReason(grant.ExpiresAt).ShouldBe(DownloadUnavailableReason.Expired);
    }

    [Fact]
    public void RecordDownload_Revoked_ReturnsDownloadUnavailable()
    {
        var grant = NewGrant();
        grant.Revoke(_paidAt);

        var result = grant.RecordDownload(_paidAt);

        result.Error.Code.ShouldBe("download_unavailable");
        grant.GetUnavailableReason(_paidAt).ShouldBe(DownloadUnavailableReason.Revoked);
    }

    [Fact]
    public void GetUnavailableReason_RevokedAndExpired_ReportsRevoked()
    {
        var grant = NewGrant();
        grant.Revoke(_paidAt);

        grant.GetUnavailableReason(grant.ExpiresAt.AddDays(1)).ShouldBe(DownloadUnavailableReason.Revoked);
    }

    [Fact]
    public void Revoke_Twice_KeepsFirstRevocationTime()
    {
        var grant = NewGrant();
        grant.Revoke(_paidAt);

        grant.Revoke(_paidAt.AddDays(1));

        grant.RevokedAt.ShouldBe(_paidAt);
    }

    private static DownloadGrant NewGrant() =>
        DownloadGrant.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), _paidAt);
}
