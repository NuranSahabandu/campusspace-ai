import { useState } from 'react'
import {
  Box,
  Chip,
  type ChipProps,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import type { AuditLogDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { campusDayBounds, formatDateTime } from '../../ui/formatDateTime'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { AUDIT_ACTIONS, AUDIT_ENTITY_TYPES, AUDIT_SORT_FIELDS, useAuditLogs } from './useAuditLogs'

const ACTION_COLORS: Record<string, ChipProps['color']> = {
  LoginFailed: 'error',
  Login: 'info',
  Deleted: 'warning',
}

/** details is {changed: [...]} for entity changes, {email} for LoginFailed, or {} (shown as "—"). */
function Details({ details }: { details: Record<string, unknown> }) {
  const { changed, ...rest } = details
  const others = Object.entries(rest)
  const names = Array.isArray(changed) ? changed.map(String) : []
  if (names.length === 0 && others.length === 0) return <>—</>
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', flexWrap: 'wrap', height: '100%' }}>
      {names.map((name) => (
        <Chip key={name} size="small" variant="outlined" label={name} />
      ))}
      {others.map(([key, value]) => (
        <Typography key={key} variant="body2" component="span">
          {key}: {typeof value === 'string' ? value : JSON.stringify(value)}
        </Typography>
      ))}
    </Stack>
  )
}

const columns: GridColDef<AuditLogDto>[] = [
  { field: 'at', headerName: 'Time', width: 190, valueFormatter: (value: string) => formatDateTime(value) },
  {
    field: 'userName',
    headerName: 'User',
    flex: 1,
    minWidth: 150,
    sortable: false,
    valueFormatter: (value: string | null) => value ?? '—',
  },
  {
    field: 'action',
    headerName: 'Action',
    width: 130,
    sortable: false,
    renderCell: ({ value }) => <Chip size="small" label={value} color={ACTION_COLORS[value] ?? 'default'} />,
  },
  { field: 'entityType', headerName: 'Entity', width: 120, sortable: false },
  {
    field: 'entityId',
    headerName: 'Entity id',
    width: 100,
    sortable: false,
    valueFormatter: (value: string | null) => value ?? '—',
  },
  {
    field: 'details',
    headerName: 'Details',
    flex: 2,
    minWidth: 220,
    sortable: false,
    renderCell: ({ row }) => <Details details={row.details} />,
  },
]

/** The audit log (§15.3), read-only and newest first. Dates filter whole days in campus time. */
export function AuditLogsPage() {
  const table = useServerTable({ sortFields: AUDIT_SORT_FIELDS, initialSort: [{ field: 'at', sort: 'desc' }] })
  const [action, setAction] = useState('')
  const [entityType, setEntityType] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  // YYYY-MM-DD strings compare correctly as text.
  const rangeError = fromDate && toDate && fromDate > toDate ? 'From must not be later than To' : undefined

  const query = useAuditLogs(
    {
      ...table.params,
      action,
      entityType,
      from: fromDate ? campusDayBounds(fromDate).from : undefined,
      to: toDate ? campusDayBounds(toDate).to : undefined,
    },
    { enabled: !rangeError },
  )

  const filter = (set: (value: string) => void) => (value: string) => {
    set(value)
    table.resetPage()
  }

  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        Audit log
      </Typography>

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mb: 2 }}>
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="action-filter-label">Action</InputLabel>
          <Select
            labelId="action-filter-label"
            label="Action"
            value={action}
            onChange={(e) => filter(setAction)(e.target.value)}
          >
            <MenuItem value="">All actions</MenuItem>
            {AUDIT_ACTIONS.map((a) => (
              <MenuItem key={a} value={a}>
                {a}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="entity-filter-label">Entity</InputLabel>
          <Select
            labelId="entity-filter-label"
            label="Entity"
            value={entityType}
            onChange={(e) => filter(setEntityType)(e.target.value)}
          >
            <MenuItem value="">All entities</MenuItem>
            {AUDIT_ENTITY_TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {t}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <TextField
          label="From"
          type="date"
          size="small"
          value={fromDate}
          onChange={(e) => filter(setFromDate)(e.target.value)}
          error={!!rangeError}
          helperText={rangeError}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          label="To"
          type="date"
          size="small"
          value={toDate}
          onChange={(e) => filter(setToDate)(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      </Stack>

      <Box>
        <ServerDataGrid
          query={query}
          columns={columns}
          gridProps={table.gridProps}
          noun="the audit log"
          emptyText="No audit entries match"
        />
      </Box>
    </>
  )
}
