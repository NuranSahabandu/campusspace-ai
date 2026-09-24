namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of role names. Used by the Users.Role CHECK constraint and by [Authorize(Roles = ...)].
/// </summary>
public static class Roles
{
    public const string Student = "Student";
    public const string Lecturer = "Lecturer";
    public const string LabTechnician = "LabTechnician";
    public const string FacilitiesOfficer = "FacilitiesOfficer";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All =
        [Student, Lecturer, LabTechnician, FacilitiesOfficer, Admin];
}
