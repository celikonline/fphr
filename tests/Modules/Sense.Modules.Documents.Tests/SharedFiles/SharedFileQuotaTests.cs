using Sense.Modules.Documents.Domain.SharedFiles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests.SharedFiles;

public sealed class SharedFileQuotaTests
{
    [Fact]
    public void Calculate_ZeroUsage_PercentIsZero_AndNotExceeded()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 0, quotaBytes: 100);

        quota.UsedPercent.ShouldBe(0);
        quota.IsExceeded.ShouldBeFalse();
    }

    [Fact]
    public void Calculate_PartialUsage_ComputesPercent()
    {
        // Card acceptance example: "%22 (22.88 MB / 104.86 MB)"
        var used = (long)(22.88 * 1024 * 1024);
        var quotaBytes = (long)(104.86 * 1024 * 1024);

        var quota = SharedFileQuota.Calculate(used, quotaBytes);

        quota.UsedPercent.ShouldBe(21.82, tolerance: 0.5);
        quota.IsExceeded.ShouldBeFalse();
    }

    [Fact]
    public void Calculate_AtQuota_IsExceededTrue_PercentClampedTo100()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 100, quotaBytes: 100);

        quota.UsedPercent.ShouldBe(100);
        quota.IsExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Calculate_OverQuota_PercentClampedTo100_AndExceeded()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 150, quotaBytes: 100);

        quota.UsedPercent.ShouldBe(100);
        quota.IsExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Calculate_ZeroQuota_WithNoUsage_IsNotExceeded()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 0, quotaBytes: 0);

        quota.IsExceeded.ShouldBeFalse();
        quota.UsedPercent.ShouldBe(0);
    }

    [Fact]
    public void Calculate_ZeroQuota_WithAnyUsage_IsExceeded_AndDoesNotThrow()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 1, quotaBytes: 0);

        quota.IsExceeded.ShouldBeTrue();
        quota.UsedPercent.ShouldBe(100);
    }

    [Fact]
    public void WouldExceed_WhenAdditionalBytesFitWithinRemainingQuota_ReturnsFalse()
    {
        var quota = SharedFileQuota.Calculate(usedBytes: 50, quotaBytes: 100);

        quota.WouldExceed(50).ShouldBeFalse(); // exactly fills quota - allowed
        quota.WouldExceed(51).ShouldBeTrue();
    }
}
