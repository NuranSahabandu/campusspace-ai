namespace CampusSpace.Api.Models;

/// <summary>
/// One priced line of a quotation. UnitPrice is a snapshot (as in Lecture 04's order_items.unit_price): a later change to
/// a pricing rule or an equipment fee doesn't change a saved quote. LineTotal = round(Qty × UnitPrice, 2).
/// </summary>
public class QuotationLine : ITimestamped
{
    public long Id { get; set; }
    public long QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;
    /// <summary>One of <see cref="QuotationLineKinds.All"/>.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Set for Equipment lines only.</summary>
    public long? EquipmentTypeId { get; set; }
    public EquipmentType? EquipmentType { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
