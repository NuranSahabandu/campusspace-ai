namespace CampusSpace.Api.Middleware;

/// <summary>
/// Thrown by services when a request is well-formed but breaks a business rule that annotations cannot check
/// (for example "an Admin cannot deactivate themselves"). GlobalExceptionHandler maps it to a 400 validation
/// Problem Details with <c>errors: { Field: [Message] }</c>, so clients show it next to the field.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string field, string message) : base(message)
    {
        Field = field;
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }

    /// <summary>Several fields at once (a policy PUT reports every bad key). The first error is the title.</summary>
    public BusinessRuleException(IReadOnlyDictionary<string, string[]> errors)
        : base(errors.Count > 0 ? errors.First().Value[0] : throw new ArgumentException("No errors.", nameof(errors)))
    {
        Field = errors.First().Key;
        Errors = errors;
    }

    /// <summary>The first field with an error.</summary>
    public string Field { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
