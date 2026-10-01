namespace CampusSpace.Api.Models;

/// <summary>
/// One item handed over for a booking (§8.1 Component B, UC10/UC11). Created by checkout and closed by check-in; never
/// deleted. An open loan (CheckedInAt null) is what makes the item OnLoan. IX_EquipmentLoans_ItemId_Open guarantees
/// that an item has at most one open loan. DueAt is the booking's end.
/// </summary>
public class EquipmentLoan : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public long ItemId { get; set; }
    public EquipmentItem Item { get; set; } = null!;
    public DateTime CheckedOutAt { get; set; }
    public long CheckedOutById { get; set; }
    public User CheckedOutBy { get; set; } = null!;
    public DateTime DueAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public long? CheckedInById { get; set; }
    public User? CheckedInBy { get; set; }
    /// <summary>One of <see cref="EquipmentConditions.All"/>; set at check-in.</summary>
    public string? ReturnCondition { get; set; }
    public string? DamageNote { get; set; }
    /// <summary>The photo's object key in the photo store (<see cref="Photos.PhotoKeys"/>; never a URL or a path).</summary>
    public string? DamagePhotoKey { get; set; }
    /// <summary>image/jpeg or image/png, from the magic bytes at upload; served as the photo's Content-Type.</summary>
    public string? DamagePhotoContentType { get; set; }
    /// <summary>The photo's size in bytes. Null only for a legacy photo until LegacyPhotoImporter fills it.</summary>
    public int? DamagePhotoSizeBytes { get; set; }
    /// <summary>Checked in after DueAt. Flagged, not charged.</summary>
    public bool IsLateReturn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
