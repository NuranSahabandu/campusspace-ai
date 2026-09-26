namespace CampusSpace.Api.Middleware;

/// <summary>
/// Thrown by services when a request is well-formed but breaks a business rule that annotations cannot check
/// (for example "an Admin cannot deactivate themselves"). GlobalExceptionHandler maps it to a 400 validation
/// Problem Details with <c>errors: { Field: [Message] }</c>, so clients show it next to the field.
/// </summary>
public sealed class BusinessRuleException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
