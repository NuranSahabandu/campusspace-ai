import type { ReactNode } from 'react'
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined'
import { Box, Paper, Stack, Tooltip, Typography } from '@mui/material'
import type { RunMetricsDto } from '../../api/types'
import { formatMs, formatRate, formatTokens } from './format'

const PROCESSING_HELP =
  'Agent processing time: the sum of a run’s step durations up to and including the approval gate (the finalize ' +
  'step after an approval is left out). A revised run is one run, so it covers every plan cycle. It does not include ' +
  'graph or poller overhead between steps, or the officer’s wait.'

const SUCCESS_HELP =
  'Runs that reached the approval gate ÷ finished runs. A run that reached the gate and then failed at approval counts ' +
  'as a success; runs still working (queued, running, or a revision being prepared) are left out.'

const FALLBACK_HELP =
  'Runs with at least one step that fell back from the LLM to its stub ÷ runs with at least one LLM-attempted step. ' +
  'With every agent on stubs the rate is — (0 of 0).'

export function MetricCard({ title, help, value, children }: { title: string; help: string; value: string; children: ReactNode }) {
  return (
    <Paper component="section" aria-label={title} variant="outlined" sx={{ p: 2, minWidth: 0 }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
        <Typography variant="subtitle2" component="h2" color="text.secondary">
          {title}
        </Typography>
        <Tooltip title={help}>
          <InfoOutlinedIcon fontSize="inherit" color="action" aria-label={`About ${title}`} />
        </Tooltip>
      </Stack>
      <Typography variant="h5" component="p" sx={{ my: 0.5 }}>
        {value}
      </Typography>
      <Box sx={{ color: 'text.secondary', typography: 'body2' }}>{children}</Box>
    </Paper>
  )
}

/** Avg and p95 with the bucket's denominator, e.g. "avg 3.1 s · p95 16.4 s (15 runs)". */
const processing = (avg: number | null, p95: number | null, runs: number) =>
  runs === 0 ? '— (0 runs)' : `avg ${formatMs(avg)} · p95 ${formatMs(p95)} (${runs} ${runs === 1 ? 'run' : 'runs'})`

/** The run-level cards (plan §12 KPIs for agents). Every value shows its denominator; 0 of 0 shows "—", never 0%. */
export function MetricsCards({ runs }: { runs: RunMetricsDto }) {
  const gate = runs.reachedGateProcessing
  const failed = runs.failedBeforeGateProcessing
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: 'repeat(4, 1fr)' } }}>
      <MetricCard title="Success rate" help={SUCCESS_HELP} value={formatRate(runs.successRate, runs.reachedGate, runs.finished)}>
        {runs.total} runs · {runs.inProgress} in progress · {runs.failedBeforeGate} failed before the gate
      </MetricCard>
      <MetricCard title="Agent processing time" help={PROCESSING_HELP} value={processing(gate.avgMs, gate.p95Ms, gate.runs)}>
        <div>Reached the gate{gate.withoutSteps ? ` · ${gate.withoutSteps} without steps` : ''}</div>
        <div>
          Failed runs: {processing(failed.avgMs, failed.p95Ms, failed.runs)}
          {failed.withoutSteps ? ` · ${failed.withoutSteps} without steps` : ''}
        </div>
      </MetricCard>
      <MetricCard
        title="Avg tokens per run"
        help="Total tokens reported by the LLM steps ÷ runs with any usage. Stub runs report none."
        value={formatTokens(runs.avgTokensPerRun)}
      >
        {formatTokens(runs.totalTokens)} tokens over {runs.runsWithUsage} {runs.runsWithUsage === 1 ? 'run' : 'runs'} with usage
      </MetricCard>
      <MetricCard
        title="Fallback rate"
        help={FALLBACK_HELP}
        value={formatRate(runs.fallbackRate, runs.fallbackRuns, runs.llmAttemptedRuns)}
      >
        Runs with an LLM step that fell back to its stub
      </MetricCard>
    </Box>
  )
}
