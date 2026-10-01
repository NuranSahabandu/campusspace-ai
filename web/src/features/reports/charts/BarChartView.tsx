import { BarChart } from '@mui/x-charts/BarChart'

/** One bar. A null value (for example a building with no available hours) draws no bar. */
export interface BarDatum {
  label: string
  value: number | null
}

export interface BarChartViewProps {
  data: BarDatum[]
  /** Names the single series in the tooltip (the panel title names the chart, so there is no legend). */
  valueLabel: string
  formatValue: (value: number) => string
  /** A fixed axis maximum, e.g. 100 for percentages. */
  max?: number
  height?: number
}

/** The 5.1 series colour, validated against the light surface (dataviz validator: all checks pass). */
export const SERIES_COLOR = '#2f6bbf'

/**
 * The only module that imports the chart library. It is loaded with React.lazy from ChartPanel, so @mui/x-charts lands
 * in its own chunk and never in the main bundle.
 */
export default function BarChartView({ data, valueLabel, formatValue, max, height = 240 }: BarChartViewProps) {
  const format = (value: number | null) => (value === null ? '—' : formatValue(value))
  return (
    <BarChart
      height={height}
      hideLegend
      borderRadius={4}
      grid={{ horizontal: true }}
      xAxis={[{ scaleType: 'band', data: data.map((d) => d.label) }]}
      yAxis={[{ min: 0, max, tickMinStep: 1, valueFormatter: (v: number) => formatValue(v) }]}
      series={[{ data: data.map((d) => d.value), label: valueLabel, color: SERIES_COLOR, valueFormatter: format }]}
      margin={{ left: 8, right: 8 }}
    />
  )
}
