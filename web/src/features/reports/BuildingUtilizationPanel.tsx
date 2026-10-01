import type { BuildingUtilizationDto, UtilizationFigures } from '../../api/types'
import { formatPercent } from '../agentRuns/format'
import { ChartPanel } from './ChartPanel'
import { formatHours, formatUtilization } from './format'

/** Utilization by building: bars in %, the table with booked and available hours (each building = Σ booked ÷ Σ available). */
export function BuildingUtilizationPanel({
  buildings,
  overall,
  title = 'Utilization by building',
}: {
  buildings: BuildingUtilizationDto[]
  overall: UtilizationFigures
  title?: string
}) {
  return (
    <ChartPanel
      title={title}
      summary={`Overall ${formatUtilization(overall)} across ${buildings.length} ${buildings.length === 1 ? 'building' : 'buildings'}.`}
      data={buildings.map((b) => ({
        label: b.code,
        value: b.figures.utilization === null ? null : Math.round(b.figures.utilization * 1000) / 10,
      }))}
      valueLabel="Utilization"
      formatValue={(v) => `${v}%`}
      max={100}
      rows={buildings}
      rowKey={(b) => b.buildingId}
      columns={[
        { header: 'Building', cell: (b) => `${b.code} · ${b.name}` },
        { header: 'Rooms', cell: (b) => b.rooms, numeric: true },
        { header: 'Booked', cell: (b) => formatHours(b.figures.bookedHours), numeric: true },
        { header: 'Available', cell: (b) => formatHours(b.figures.availableHours), numeric: true },
        { header: 'Utilization', cell: (b) => formatPercent(b.figures.utilization), numeric: true },
      ]}
      emptyText={buildings.length === 0 ? 'No active rooms.' : undefined}
    />
  )
}
