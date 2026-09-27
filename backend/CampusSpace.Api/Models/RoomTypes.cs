namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of room types. Used by the Rooms.Type CHECK constraint and by request validation.
/// </summary>
public static class RoomTypes
{
    public const string LectureHall = "LectureHall";
    public const string ComputerLab = "ComputerLab";
    public const string SeminarRoom = "SeminarRoom";
    public const string Auditorium = "Auditorium";

    public static readonly IReadOnlyList<string> All = [LectureHall, ComputerLab, SeminarRoom, Auditorium];

    /// <summary>A readable name for quotation lines ("Computer lab"). Unknown values are returned unchanged.</summary>
    public static string Label(string type) => type switch
    {
        LectureHall => "Lecture hall",
        ComputerLab => "Computer lab",
        SeminarRoom => "Seminar room",
        _ => type,
    };
}
