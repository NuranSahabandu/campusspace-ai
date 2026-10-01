import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import RefreshIcon from '@mui/icons-material/Refresh'
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Skeleton,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import { useQueryClient } from '@tanstack/react-query'
import { parseProblem } from '../../api/problem'
import type { AgentRunListItemDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { campusAddDays, campusToday, formatDateTime } from '../../ui/formatDateTime'
import { AGENT_RUN_STATUSES, RUN_STATUS_COLORS, isAgentRunStatus } from '../approvals/agentRuns'
import { AgentMetricsTable } from './AgentMetricsTable'
import { formatMs, formatTokens } from './format'
import { LatencyBars } from './LatencyBars'
import { MetricsCards } from './MetricsCards'
import {
  AGENT_RUNS_SORT_FIELDS,
  type AgentRunsParams,
  agentRunsKeys,
  useAgentRunMetrics,
  useAgentRunsList,
} from './useAgentRuns'

const DATE = /^\d{4}-\d{2}-\d{2}$/
const parseDate = (raw: string | null) => (raw !== null && DATE.test(raw) ? raw : undefined)
const parseId = (raw: string | null) => (raw !== null && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

/** The default range: the last 7 campus days, today included. */
export const DEFAULT_RANGE_DAYS = 7

type FallbackFilter = 'any' | 'yes' | 'no'
const FALLBACK_PARAM: Record<FallbackFilter, string | undefined> = { any: undefined, yes: 'true', no: 'false' }

/**
 * The agent runs monitor (UC23, plan §12): metrics cards and the per-agent table for a campus-date range, and every run
 * with filters. All filters live in the URL, so opening a run and coming back keeps them. A run waiting for a decision
 * opens the approval page; any other run opens its trace.
 */
export function AgentRunsPage() {
  const table = useServerTable({
    sortFields: AGENT_RUNS_SORT_FIELDS,
    initialSort: [{ field: 'createdAt', sort: 'desc' }],
    urlState: true,
  })
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const today = campusToday()
  const from = parseDate(searchParams.get('from')) ?? campusAddDays(today, -(DEFAULT_RANGE_DAYS - 1))
  const to = parseDate(searchParams.get('to')) ?? today
  const rangeInvalid = to < from
  const statuses = [...new Set(searchParams.getAll('status').filter(isAgentRunStatus))]
  const rawFallback = searchParams.get('fallback')
  const fallback: FallbackFilter = rawFallback === 'true' ? 'yes' : rawFallback === 'false' ? 'no' : 'any'
  const requestId = parseId(searchParams.get('requestId'))
  const [requestIdText, setRequestIdText] = useState(requestId !== undefined ? String(requestId) : '')

  const params: AgentRunsParams = {
    ...table.params,
    status: statuses,
    from,
    to,
    requestId,
    fallback: fallback === 'any' ? undefined : fallback === 'yes',
  }

  // While To is before From nothing is sent: the grid and the cards keep the last valid results.
  const paramsKey = JSON.stringify(params)
  const [lastValid, setLastValid] = useState(rangeInvalid ? null : { key: paramsKey, params })
  if (!rangeInvalid && lastValid?.key !== paramsKey) setLastValid({ key: paramsKey, params })
  const effective = rangeInvalid ? lastValid?.params : params
  const list = useAgentRunsList(effective ?? params, { enabled: effective !== undefined })
  const metrics = useAgentRunMetrics(
    { from: effective?.from ?? from, to: effective?.to ?? to },
    { enabled: effective !== undefined },
  )

  const setParam = (key: string, value: string | undefined) =>
    table.updateUrl((p) => {
      if (value) p.set(key, value)
      else p.delete(key)
    })

  const setStatuses = (values: string[]) =>
    table.updateUrl((p) => {
      p.delete('status')
      values.filter(isAgentRunStatus).forEach((s) => p.append('status', s))
    })

  const onRequestIdChange = (text: string) => {
    const digits = text.replace(/\D/g, '')
    setRequestIdText(digits)
    setParam('requestId', parseId(digits) !== undefined ? digits : undefined)
  }

  const openRun = (row: AgentRunListItemDto) =>
    navigate(row.status === 'AwaitingApproval' ? `/approvals/${row.requestId}` : `/agent-runs/${row.id}`)

  const columns: GridColDef<AgentRunListItemDto>[] = [
    { field: 'createdAt', headerName: 'Created', width: 170, valueFormatter: (value: string) => formatDateTime(value) },
    {
      field: 'request',
      headerName: 'Request',
      flex: 1,
      minWidth: 220,
      sortable: false,
      renderCell: ({ row }) => (
        <Stack sx={{ justifyContent: 'center', height: '100%', lineHeight: 1.3, minWidth: 0 }}>
          <Typography variant="body2" noWrap title={row.purpose}>
            {row.purpose}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap>
            Request #{row.requestId} · revision {row.revisionNo}
          </Typography>
        </Stack>
      ),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 170,
      renderCell: ({ row }) => (
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', height: '100%' }}>
          <Chip size="small" variant="outlined" color={RUN_STATUS_COLORS[row.status] ?? 'default'} label={row.status} />
          {row.anyFallback && <Chip size="small" color="warning" label="Fallback" />}
        </Stack>
      ),
    },
    {
      field: 'model',
      headerName: 'Model',
      width: 200,
      sortable: false,
      renderCell: ({ row }) => (
        <Box component="span" title={row.model ?? undefined} sx={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {row.model ?? '—'}
        </Box>
      ),
    },
    {
      field: 'steps',
      headerName: 'Steps / tools',
      width: 110,
      sortable: false,
      valueGetter: (_value, row) => `${row.stepCount} / ${row.toolCallCount}`,
    },
    {
      field: 'totalTokens',
      headerName: 'Tokens',
      type: 'number',
      width: 100,
      sortable: false,
      valueFormatter: (value: number | null) => formatTokens(value),
    },
    {
      field: 'durationMs',
      headerName: 'Wall time (incl. officer wait)',
      type: 'number',
      width: 200,
      valueFormatter: (value: number | null) => formatMs(value),
    },
    {
      field: 'failureReason',
      headerName: 'Failure reason',
      flex: 1,
      minWidth: 220,
      sortable: false,
      // Plain text: a React text child, never HTML.
      renderCell: ({ row }) => (
        <Box component="span" title={row.failureReason ?? undefined} sx={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {row.failureReason ?? ''}
        </Box>
      ),
    },
  ]

  return (
    <>
      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="h4" component="h1">
          Agent runs
        </Typography>
        <Button
          startIcon={<RefreshIcon />}
          sx={{ ml: 'auto' }}
          onClick={() => void queryClient.invalidateQueries({ queryKey: agentRunsKeys.all })}
        >
          Refresh
        </Button>
      </Stack>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <TextField
          label="From"
          type="date"
          size="small"
          value={from}
          onChange={(e) => setParam('from', e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          label="To"
          type="date"
          size="small"
          value={to}
          onChange={(e) => setParam('to', e.target.value)}
          error={rangeInvalid}
          helperText={rangeInvalid ? 'To must be on or after From' : 'Campus dates, on when the run was created'}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: from } }}
        />
      </Stack>

      <MetricsSection query={metrics} />

      <Typography variant="h6" component="h2" sx={{ mt: 3, mb: 1 }}>
        Runs
      </Typography>
      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 220 }}>
          <InputLabel id="agent-run-status-label">Status</InputLabel>
          <Select
            labelId="agent-run-status-label"
            label="Status"
            multiple
            value={statuses}
            onChange={(e) => setStatuses(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)}
            renderValue={(selected) => selected.join(', ')}
          >
            {AGENT_RUN_STATUSES.map((s) => (
              <MenuItem key={s} value={s}>
                {s}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <ToggleButtonGroup
          exclusive
          size="small"
          aria-label="Fallback"
          value={fallback}
          onChange={(_e, value: FallbackFilter | null) => {
            if (value !== null) setParam('fallback', FALLBACK_PARAM[value])
          }}
        >
          <ToggleButton value="any">Any</ToggleButton>
          <ToggleButton value="yes">Fell back</ToggleButton>
          <ToggleButton value="no">No fallback</ToggleButton>
        </ToggleButtonGroup>
        <TextField
          label="Request #"
          size="small"
          value={requestIdText}
          onChange={(e) => onRequestIdChange(e.target.value)}
          sx={{ width: 130 }}
          slotProps={{ htmlInput: { inputMode: 'numeric', 'aria-label': 'Request number' } }}
        />
        <TextField
          label="Search"
          placeholder="Request purpose"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 240 }}
          slotProps={{ htmlInput: { 'aria-label': 'Request purpose' } }}
        />
      </Stack>

      <ServerDataGrid
        query={list}
        columns={columns}
        gridProps={table.gridProps}
        noun="agent runs"
        emptyText="No agent runs match these filters"
        rowHeight={60}
        onRowClick={openRun}
      />
    </>
  )
}

function MetricsSection({ query }: { query: ReturnType<typeof useAgentRunMetrics> }) {
  if (query.isError)
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => query.refetch()}>
            Retry
          </Button>
        }
      >
        Could not load the agent metrics: {parseProblem(query.error).title}
      </Alert>
    )
  if (query.isPending) return <Skeleton variant="rectangular" height={160} aria-label="Loading agent metrics" />
  const { runs, agents } = query.data
  return (
    <Stack spacing={2}>
      {runs.total === 0 && <Alert severity="info">No agent runs in this range</Alert>}
      <MetricsCards runs={runs} />
      {agents.length > 0 && (
        <Paper component="section" aria-label="Per-agent metrics" variant="outlined" sx={{ p: 2, minWidth: 0 }}>
          <Typography variant="subtitle2" component="h2" color="text.secondary" sx={{ mb: 1 }}>
            Per agent
          </Typography>
          <Box sx={{ mb: 2, maxWidth: 720 }}>
            <LatencyBars agents={agents} />
          </Box>
          <AgentMetricsTable agents={agents} />
        </Paper>
      )}
    </Stack>
  )
}
