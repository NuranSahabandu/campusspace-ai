namespace CampusSpace.Api.Middleware;

/// <summary>
/// Thrown by services when a signed-in caller has the right role but fails an object-level check (§15.1), for example
/// a student reading another student's request. GlobalExceptionHandler maps it to 403 with the message as the title.
/// </summary>
public sealed class ForbiddenException(string message) : Exception(message);
