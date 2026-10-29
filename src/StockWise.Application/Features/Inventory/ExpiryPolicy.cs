using StockWise.Application.Common;

namespace StockWise.Application.Features.Inventory;

/// <summary>
/// Expiry classification policy: a batch is valid through its expiry calendar day
/// (in the organization's time zone) and counts as expired from the next day on.
/// </summary>
public static class ExpiryPolicy
{
    public static string Classify(int daysToExpiry, int warningDays) =>
        daysToExpiry < 0 ? "expired"
        : daysToExpiry <= 7 ? "critical"
        : daysToExpiry <= warningDays ? "warning"
        : "ok";

    public static bool IsExpired(int daysToExpiry) => daysToExpiry < 0;
}
