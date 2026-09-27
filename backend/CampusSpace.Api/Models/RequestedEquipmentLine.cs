namespace CampusSpace.Api.Models;

/// <summary>Portable equipment the requester asked for: one line per type.</summary>
public class RequestedEquipmentLine
{
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public long TypeId { get; set; }
    public EquipmentType Type { get; set; } = null!;
    public int Quantity { get; set; }
}
