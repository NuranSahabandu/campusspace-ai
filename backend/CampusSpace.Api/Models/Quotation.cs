namespace CampusSpace.Api.Models;

/// <summary>
/// The price of one booking request (§8.1 Component D), computed by IQuotationCalculator. Total = Subtotal - Discount.
/// An exempt quote (the lecturer exemption) keeps its lines priced and has Discount = Subtotal. At most one Draft or
/// Issued quote per request. AgentRunId is the run whose proposal it prices (null for quotes made outside a run).
/// </summary>
public class Quotation : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public Guid? AgentRunId { get; set; }
    public AgentRun? AgentRun { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string? DiscountReason { get; set; }
    public bool IsExempt { get; set; }
    /// <summary>One of <see cref="QuotationStatuses.All"/>.</summary>
    public string Status { get; set; } = QuotationStatuses.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<QuotationLine> Lines { get; set; } = [];
}
