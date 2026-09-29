import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { Accordion, AccordionDetails, AccordionSummary, Box, Chip, Stack, Typography } from '@mui/material'
import type { AgentRunDetailDto, AgentStepDto, JsonValue } from '../../api/types'
import { formatDateTime } from '../../ui/formatDateTime'

const ms = (value: number | null) => (value === null ? '—' : value < 1000 ? `${value} ms` : `${(value / 1000).toFixed(1)} s`)

/** A trace value, pretty-printed as plain text (never rendered as HTML). */
export function JsonText({ value, label }: { value: JsonValue | null; label: string }) {
  return (
    <Box
      component="pre"
      aria-label={label}
      sx={{
        m: 0,
        p: 1,
        bgcolor: 'action.hover',
        borderRadius: 1,
        fontSize: 12,
        whiteSpace: 'pre-wrap',
        overflowWrap: 'anywhere',
        maxHeight: 320,
        overflow: 'auto',
      }}
    >
      {value === null ? 'null' : JSON.stringify(value, null, 2)}
    </Box>
  )
}

function Step({ step }: { step: AgentStepDto }) {
  const ok = step.status === 'Succeeded'
  return (
    <Accordion disableGutters variant="outlined">
      <AccordionSummary expandIcon={<ExpandMoreIcon />} aria-label={`Step ${step.sequence}: ${step.agentName}`}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography sx={{ fontWeight: 600 }}>
            {step.sequence}. {step.agentName}
          </Typography>
          <Chip size="small" color={ok ? 'success' : 'error'} variant="outlined" label={step.status} />
          <Typography variant="body2" color="text.secondary">
            {ms(step.durationMs)} · {step.toolCalls.length} tool call{step.toolCalls.length === 1 ? '' : 's'}
            {step.retries > 0 ? ` · ${step.retries} retr${step.retries === 1 ? 'y' : 'ies'}` : ''}
          </Typography>
        </Stack>
      </AccordionSummary>
      <AccordionDetails sx={{ display: 'grid', gap: 1.5 }}>
        {step.error && <Typography color="error">{step.error}</Typography>}
        <div>
          <Typography variant="caption" color="text.secondary">
            Input
          </Typography>
          <JsonText value={step.input} label={`Step ${step.sequence} input`} />
        </div>
        <div>
          <Typography variant="caption" color="text.secondary">
            Output
          </Typography>
          <JsonText value={step.output} label={`Step ${step.sequence} output`} />
        </div>
        {step.toolCalls.map((call, i) => (
          <Box key={`${call.toolName}-${i}`} sx={{ borderLeft: 2, borderColor: 'divider', pl: 1.5 }}>
            <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 0.5 }}>
              <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 600 }}>
                {call.toolName}
              </Typography>
              <Chip
                size="small"
                color={call.succeeded ? 'success' : 'error'}
                variant="outlined"
                label={call.succeeded ? 'OK' : 'Failed'}
              />
              <Typography variant="caption" color="text.secondary">
                {ms(call.durationMs)}
              </Typography>
            </Stack>
            {call.error && <Typography variant="body2" color="error">{call.error}</Typography>}
            <Box sx={{ display: 'grid', gap: 1, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
              <JsonText value={call.args} label={`${call.toolName} arguments`} />
              <JsonText value={call.resultSummary} label={`${call.toolName} result`} />
            </Box>
          </Box>
        ))}
      </AccordionDetails>
    </Accordion>
  )
}

/**
 * The run's trajectory (nodes in order), then each step, expandable to its tool calls with arguments and result
 * summaries as pretty-printed JSON text, with statuses and durations. Summaries, inputs, outputs and timings only.
 */
export function AgentTimeline({ run }: { run: AgentRunDetailDto }) {
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        Revision {run.revisionNo} · {run.status} · {ms(run.durationMs)}
        {run.model ? ` · ${run.model}` : ''}
        {run.startedAt ? ` · started ${formatDateTime(run.startedAt)}` : ''}
      </Typography>
      {run.officerSummary && (
        <div>
          <Typography variant="overline" color="text.secondary">
            Officer summary
          </Typography>
          <Typography data-testid="officer-summary" sx={{ whiteSpace: 'pre-wrap' }}>
            {run.officerSummary}
          </Typography>
        </div>
      )}
      {run.failureReason && <Typography color="error">{run.failureReason}</Typography>}
      <Box component="ol" aria-label="Agent trajectory" sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, m: 0, p: 0, listStyle: 'none' }}>
        {run.nodes.map((node, i) => (
          <Box component="li" key={`${node}-${i}`} sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            {i > 0 && <span aria-hidden>→</span>}
            <Chip size="small" label={node} />
          </Box>
        ))}
      </Box>
      <div>
        {run.steps.length ? (
          run.steps.map((step) => <Step key={step.sequence} step={step} />)
        ) : (
          <Typography color="text.secondary">No steps recorded yet</Typography>
        )}
      </div>
      {run.decisions.length > 0 && (
        <div>
          <Typography variant="overline" color="text.secondary">
            Officer decisions
          </Typography>
          {run.decisions.map((d, i) => (
            <Box key={`${d.decidedAt}-${i}`} sx={{ mb: 1 }}>
              <Typography variant="body2">
                {d.decision} by {d.officerName} · {formatDateTime(d.decidedAt)}
              </Typography>
              {d.comment && (
                <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-wrap' }}>
                  {d.comment}
                </Typography>
              )}
            </Box>
          ))}
        </div>
      )}
    </Box>
  )
}
