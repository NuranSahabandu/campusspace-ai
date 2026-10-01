import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import type { AgentMetricsDto } from '../../api/types'
import { formatMs, formatRate, formatTokens } from './format'

/**
 * Per-agent metrics (plan §10.11, a GROUP BY on AgentSteps). Each rate shows its own denominator: failure and LLM share
 * over all steps, fallback over LLM-attempted steps (llm + fallback), tokens over steps that reported usage.
 */
export function AgentMetricsTable({ agents }: { agents: AgentMetricsDto[] }) {
  if (!agents.length) return <Typography color="text.secondary">No agent steps in this range</Typography>
  return (
    <TableContainer>
      <Table size="small" aria-label="Per-agent metrics">
        <TableHead>
          <TableRow>
            <TableCell>Agent</TableCell>
            <TableCell align="right">Steps</TableCell>
            <TableCell align="right">Avg</TableCell>
            <TableCell align="right">p95</TableCell>
            <TableCell>Failure rate</TableCell>
            <TableCell>LLM share</TableCell>
            <TableCell>Fallback rate</TableCell>
            <TableCell align="right">Skipped</TableCell>
            <TableCell>Avg tokens / step</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {agents.map((a) => (
            <TableRow key={a.agent}>
              <TableCell component="th" scope="row">
                {a.agent}
              </TableCell>
              <TableCell align="right">{a.steps}</TableCell>
              <TableCell align="right">{formatMs(a.avgMs)}</TableCell>
              <TableCell align="right">{formatMs(a.p95Ms)}</TableCell>
              <TableCell>{formatRate(a.failureRate, a.failedSteps, a.steps)}</TableCell>
              <TableCell>{formatRate(a.llmShare, a.llmSteps, a.steps)}</TableCell>
              <TableCell>{formatRate(a.fallbackRate, a.fallbackSteps, a.llmAttemptedSteps)}</TableCell>
              <TableCell align="right">{a.skippedSteps}</TableCell>
              <TableCell>
                {formatTokens(a.avgTokensPerStep)} ({a.stepsWithUsage} with usage)
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}
