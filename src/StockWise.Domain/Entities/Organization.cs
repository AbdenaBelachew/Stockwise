using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

/// <summary>A tenant (business organization) in the SaaS.</summary>
public class Organization : Entity, ICreatable
{
    public string Name { get; set; } = default!;
    public string? Industry { get; set; }
    public string Country { get; set; } = default!;
    /// <summary>IANA time zone id used for all user-facing date calculations.</summary>
    public string TimeZoneId { get; set; } = "UTC";
    /// <summary>ISO 4217 currency code for display and default transactions.</summary>
    public string CurrencyCode { get; set; } = "USD";
    /// <summary>Display date format, e.g. "yyyy-MM-dd" or "dd/MM/yyyy".</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    // Operational policies / settings
    public int LowStockAlertEnabled { get; set; } = 1;
    public int ExpiryAlertDays { get; set; } = 30;
    public int[] ExpiryWarningWindows { get; set; } = [7, 30, 60, 90];
    public bool AllowsNegativeStock { get; set; }
    public bool RequireSeparationOfDuties { get; set; } = true;
    /// <summary>Purchase orders at or below this total auto-approve on submit; null/0 = manual approval always.</summary>
    public decimal? ApprovalThresholdAmount { get; set; }
    public bool AllowOverReceipt { get; set; }
    public decimal OverReceiptTolerancePercent { get; set; } = 10m;
    public bool DefaultTrackBatches { get; set; }
    public bool DefaultTrackExpiry { get; set; }
    public int MinShelfLifeDays { get; set; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class TenantMembership : Entity
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RoleId { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Role : TenantEntity
{
    public string Name { get; set; } = default!;
    public bool IsSystem { get; set; }
    public string Description { get; set; } = default!;
    public List<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission : Entity
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = default!;
}

public class Invitation : TenantEntity
{
    public string Email { get; set; } = default!;   // lowercase
    public Guid RoleId { get; set; }
    public string TokenHash { get; set; } = default!;
    public Guid InvitedByUserId { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
