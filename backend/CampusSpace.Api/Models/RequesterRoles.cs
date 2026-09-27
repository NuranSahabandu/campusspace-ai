namespace CampusSpace.Api.Models;

/// <summary>
/// The roles that submit booking requests, and so the only roles a pricing rule can apply to.
/// Used by the PricingRules.RequesterRole CHECK constraint and by request validation.
/// </summary>
public static class RequesterRoles
{
    public const string Student = Roles.Student;
    public const string Lecturer = Roles.Lecturer;

    public static readonly IReadOnlyList<string> All = [Student, Lecturer];
}
