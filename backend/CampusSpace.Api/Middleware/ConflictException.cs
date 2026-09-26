namespace CampusSpace.Api.Middleware;

/// <summary>
/// Thrown by services when a request conflicts with existing data (for example a taken email).
/// GlobalExceptionHandler maps it to 409 with the message as the Problem Details title.
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
