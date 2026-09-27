using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>
/// The only way a booking request's status changes (§8.3). Every change adds a RequestStatusHistory row to the
/// request. Nothing is saved here: the caller's SaveChanges commits the status and its history row together.
/// </summary>
public interface IRequestStateMachine
{
    /// <summary>Writes the first history row (null → Submitted) of a new request.</summary>
    void Start(BookingRequest request, long? changedById);

    /// <summary>
    /// Moves the request to <paramref name="to"/> and records who did it and why. Throws ConflictException (409)
    /// when the allowed-transitions table forbids the move.
    /// </summary>
    void Transition(BookingRequest request, string to, long? changedById, string? reason = null);
}
