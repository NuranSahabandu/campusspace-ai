import { Box, Stack, Typography } from '@mui/material'
import type { AgentMetricsDto } from '../../api/types'
import { formatMs } from './format'

/** Two series, validated as a categorical pair (dataviz validator: CVD and contrast pass on the light surface). */
const AVG_COLOR = '#2f6bbf'
const P95_COLOR = '#d97a2b'

function Bar({ value, max, color, label }: { value: number | null; max: number; color: string; label: string }) {
  const width = value === null || max === 0 ? 0 : Math.max((value / max) * 100, 0.5)
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }} title={label}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Box
          role="img"
          aria-label={label}
          sx={{ height: 10, width: `${width}%`, bgcolor: color, borderRadius: '0 4px 4px 0' }}
        />
      </Box>
      {/* Values stay in text ink; the bar beside them carries the series colour. */}
      <Typography variant="caption" sx={{ width: 64, textAlign: 'right', flexShrink: 0 }}>
        {formatMs(value)}
      </Typography>
    </Stack>
  )
}

function LegendSwatch({ color, label }: { color: string; label: string }) {
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      <Box sx={{ width: 12, height: 10, bgcolor: color, borderRadius: '0 4px 4px 0' }} />
      <Typography variant="caption">{label}</Typography>
    </Stack>
  )
}

/**
 * Avg and p95 step latency per agent as horizontal bars on one shared scale (plain MUI, no chart library). Each bar
 * shows its value as text, so the chart never relies on colour alone; the per-agent table is its table view.
 */
export function LatencyBars({ agents }: { agents: AgentMetricsDto[] }) {
  const max = Math.max(0, ...agents.flatMap((a) => [a.avgMs ?? 0, a.p95Ms ?? 0]))
  return (
    <Box component="figure" aria-label="Step latency per agent" sx={{ m: 0 }}>
      <Stack direction="row" spacing={2} sx={{ mb: 1 }} aria-hidden>
        <LegendSwatch color={AVG_COLOR} label="Average" />
        <LegendSwatch color={P95_COLOR} label="p95" />
      </Stack>
      <Stack spacing={1.25}>
        {agents.map((a) => (
          <Box
            key={a.agent}
            sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '170px 1fr' }, columnGap: 1.5, alignItems: 'center' }}
          >
            <Typography variant="body2" noWrap title={a.agent}>
              {a.agent}
            </Typography>
            {/* 2px gap between the pair of bars. */}
            <Stack spacing="2px">
              <Bar value={a.avgMs} max={max} color={AVG_COLOR} label={`${a.agent} average ${formatMs(a.avgMs)}`} />
              <Bar value={a.p95Ms} max={max} color={P95_COLOR} label={`${a.agent} p95 ${formatMs(a.p95Ms)}`} />
            </Stack>
          </Box>
        ))}
      </Stack>
    </Box>
  )
}
