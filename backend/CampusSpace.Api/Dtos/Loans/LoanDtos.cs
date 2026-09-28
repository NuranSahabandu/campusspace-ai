using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Loans;

/// <summary>POST /api/loans/checkout: hand one item over for one booking.</summary>
public record CheckoutRequest(
    [Range(1, long.MaxValue)] long BookingId,
    [Range(1, long.MaxValue)] long ItemId);

/// <summary>
/// POST /api/loans/{id}/checkin (multipart/form-data). Damaged needs a Note and a Photo (checked in the service, because
/// annotations can't depend on another field's value).
/// </summary>
public record CheckInRequest
{
    [Required, ValidEquipmentCondition] public string Condition { get; init; } = string.Empty;
    [MaxLength(EquipmentLoanConfiguration.DamageNoteMaxLength)] public string? Note { get; init; }
    public IFormFile? Photo { get; init; }
}

/// <summary>One loan. IsOverdue: still out and past DueAt. Photos are fetched from GET /api/loans/{id}/photo.</summary>
public record LoanDto(
    long Id, long BookingId, string RoomCode, long ItemId, string AssetTag, string TypeCode,
    DateTime CheckedOutAt, string CheckedOutByName, DateTime DueAt, DateTime? CheckedInAt, string? CheckedInByName,
    string? ReturnCondition, string? DamageNote, bool IsLateReturn, bool IsOverdue, bool HasPhoto);

/// <summary>
/// GET /api/loans. Overdue true: open and past due. False: everything else (returned, or not yet due). Absent: all.
/// BookingId limits the list to one booking's loans (the technician's handover screen). Search matches the asset tag or
/// type code. Newest due first unless sort=dueAt.
/// </summary>
public record LoansQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["dueAt"];

    public bool? Overdue { get; init; }

    [Range(1, long.MaxValue)] public long? BookingId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}

/// <summary>One reserved type of a handover: how many are reserved, out now, and already returned.</summary>
public record HandoverLineDto(long TypeId, string TypeCode, string TypeName, int Reserved, int Out, int Returned);

/// <summary>GET /api/loans/today: an active booking of today (campus date) that has equipment reserved.</summary>
public record HandoverDto(
    long BookingId, string RoomCode, DateTime Start, DateTime End, string RequesterName, string Status,
    IReadOnlyList<HandoverLineDto> Lines);
