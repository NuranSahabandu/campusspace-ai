import { lazy, type ReactNode, Suspense, useState } from 'react'
import BarChartIcon from '@mui/icons-material/BarChart'
import TableRowsIcon from '@mui/icons-material/TableRows'
import {
  Box,
  Paper,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import type { BarDatum } from './charts/BarChartView'

const BarChartView = lazy(() => import('./charts/BarChartView'))

export interface Column<T> {
  header: string
  cell: (row: T) => ReactNode
  numeric?: boolean
}

interface Props<T> {
  title: string
  /** One line that states what the chart shows (read by everyone, including screen readers). */
  summary: string
  data: BarDatum[]
  valueLabel: string
  formatValue: (value: number) => string
  max?: number
  rows: T[]
  rowKey: (row: T) => string | number
  columns: Column<T>[]
  /** Shown instead of the chart and the table when there is nothing to plot. */
  emptyText?: string
}

type View = 'chart' | 'table'

/**
 * A titled bar chart with its data as a table behind a Chart/Table toggle (plan §12 charts; the table is the accessible
 * view). The chart module is lazy, so the chart library is fetched only when a chart is actually shown.
 */
export function ChartPanel<T>({
  title,
  summary,
  data,
  valueLabel,
  formatValue,
  max,
  rows,
  rowKey,
  columns,
  emptyText,
}: Props<T>) {
  const [view, setView] = useState<View>('chart')
  const empty = emptyText !== undefined

  return (
    <Paper component="section" aria-label={title} variant="outlined" sx={{ p: 2, minWidth: 0 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="subtitle1" component="h2">
          {title}
        </Typography>
        {!empty && (
          <ToggleButtonGroup
            size="small"
            exclusive
            value={view}
            onChange={(_, v: View | null) => v && setView(v)}
            aria-label={`${title} view`}
          >
            <ToggleButton value="chart" aria-label="Show as chart">
              <BarChartIcon fontSize="small" />
            </ToggleButton>
            <ToggleButton value="table" aria-label="Show as table">
              <TableRowsIcon fontSize="small" />
            </ToggleButton>
          </ToggleButtonGroup>
        )}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {summary}
      </Typography>
      {empty ? (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          {emptyText}
        </Typography>
      ) : view === 'chart' ? (
        <Box role="img" aria-label={`${title}: ${summary}`}>
          <Suspense fallback={<Skeleton variant="rectangular" height={240} />}>
            <BarChartView data={data} valueLabel={valueLabel} formatValue={formatValue} max={max} />
          </Suspense>
        </Box>
      ) : (
        <TableContainer sx={{ maxHeight: 360 }}>
          <Table size="small" stickyHeader>
            <caption>{title}</caption>
            <TableHead>
              <TableRow>
                {columns.map((c) => (
                  <TableCell key={c.header} align={c.numeric ? 'right' : 'left'}>
                    {c.header}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={rowKey(row)}>
                  {columns.map((c) => (
                    <TableCell key={c.header} align={c.numeric ? 'right' : 'left'}>
                      {c.cell(row)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </Paper>
  )
}
