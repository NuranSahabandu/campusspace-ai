using CampusSpace.Api.Dtos.Reports;

namespace CampusSpace.Api.Services;

/// <summary>Officer reports (UC22, plan §9 and §12): utilization, demand and the dashboard KPIs. Read-only.</summary>
public interface IReportService
{
    Task<UtilizationReportDto> GetUtilizationAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<DemandReportDto> GetDemandAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
}
