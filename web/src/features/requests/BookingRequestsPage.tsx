import { useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import VisibilityIcon from '@mui/icons-material/Visibility'
import {
  Box,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import type { BookingRequestSummaryDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { formatCampusTimeRange, formatDateTime } from '../../ui/formatDateTime'
import { formatLkr } from '../../ui/formatLkr'
import { CancellationFlags } from './CancellationFlags'
import { RequestStatusChip } from './RequestStatusChip'
import {
  DEFAULT_STATUS_GROUP,
  REQUEST_STATUSES,
  STATUS_GROUPS,
  STATUS_GROUP_KEYS,
  type StatusGroup,
  isRequestStatus,
  isStatusGroup,
  requestStatusLabel,
} from './requestStatus'
import { BOOKING_REQUESTS_SORT_FIELDS, type BookingRequestsParams, useBookingRequests, useClubOptions } from './useRequests'

const DATE = /^\d{4}-\d{2}-\d{2}$/
const parseDate = (raw: string | null) => (raw !== null && DATE.test(raw) ? raw : undefined)
const parseId = (raw: string | null) => (raw !== null && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

/** Location state the detail page reads to link back to the list with the same filters. */
export interface FromListState {
  listSearch: string
}

/**
 * Facilities Officer work queue of booking requests (§12, Component C), read-only. Every filter, the search, sort
 * and page live in the URL query, so coming back from a request restores the list as it was. Invalid values in the
 * URL are ignored.
 */
export function BookingRequestsPage() {
  const table = useServerTable({
    sortFields: BOOKING_REQUESTS_SORT_FIELDS,
    initialSort: [{ field: 'createdAt', sort: 'desc' }],
    urlState: true,
  })
  const clubs = useClubOptions()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const location = useLocation()

  const rawGroup = searchParams.get('group')
  const group: StatusGroup = rawGroup !== null && isStatusGroup(rawGroup) ? rawGroup : DEFAULT_STATUS_GROUP
  const exactStatuses = [...new Set(searchParams.getAll('status').filter(isRequestStatus))]
  const from = parseDate(searchParams.get('from'))
  const to = parseDate(searchParams.get('to'))
  const clubId = parseId(searchParams.get('clubId'))
  const rangeInvalid = from !== undefined && to !== undefined && to < from

  const params: BookingRequestsParams = {
    ...table.params,
    // An exact status choice overrides the group.
    status: exactStatuses.length ? exactStatuses : STATUS_GROUPS[group].statuses,
    from,
    to,
    clubId,
  }

  // While To is before From nothing is sent: the grid keeps the rows of the last valid filters (same query key, so
  // no request), or shows an empty grid when nothing has loaded yet.
  const paramsKey = JSON.stringify(params)
  const [lastValid, setLastValid] = useState(rangeInvalid ? null : { key: paramsKey, params })
  if (!rangeInvalid && lastValid?.key !== paramsKey) setLastValid({ key: paramsKey, params })
  const effective = rangeInvalid ? lastValid?.params : params
  const query = useBookingRequests(effective ?? params, { enabled: effective !== undefined })

  const setParam = (key: string, value: string | undefined) =>
    table.updateUrl((p) => {
      if (value) p.set(key, value)
      else p.delete(key)
    })

  const setGroup = (value: StatusGroup) =>
    table.updateUrl((p) => {
      p.delete('status')
      if (value === DEFAULT_STATUS_GROUP) p.delete('group')
      else p.set('group', value)
    })

  const setExactStatuses = (values: string[]) =>
    table.updateUrl((p) => {
      p.delete('status')
      values.filter(isRequestStatus).forEach((s) => p.append('status', s))
    })

  const openRequest = (row: BookingRequestSummaryDto) =>
    navigate(`/requests/${row.id}`, { state: { listSearch: location.search } satisfies FromListState })

  const clubOptions = clubs.data ?? []
  const clubIdMissing = clubId !== undefined && !clubOptions.some((c) => c.id === clubId)

  const columns: GridColDef<BookingRequestSummaryDto>[] = [
    {
      field: 'purpose',
      headerName: 'Purpose',
      flex: 1.4,
      minWidth: 200,
      sortable: false,
      renderCell: ({ row }) => (
        <Box component="span" title={row.purpose} sx={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {row.purpose}
        </Box>
      ),
    },
    {
      field: 'requester',
      headerName: 'Requester',
      flex: 1,
      minWidth: 200,
      sortable: false,
      renderCell: ({ row }) => (
        <Stack sx={{ justifyContent: 'center', height: '100%', lineHeight: 1.3, minWidth: 0 }}>
          <Typography variant="body2" noWrap>
            {row.requesterName}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap>
            {row.requesterEmail}
          </Typography>
        </Stack>
      ),
    },
    {
      field: 'clubName',
      headerName: 'Club',
      width: 150,
      sortable: false,
      valueGetter: (value: string | null) => value ?? 'Academic',
    },
    {
      field: 'when',
      headerName: 'When',
      width: 250,
      valueGetter: (_value, row) => formatCampusTimeRange(row.requestedStart, row.requestedEnd),
    },
    { field: 'attendees', headerName: 'Attendees', type: 'number', width: 110 },
    {
      field: 'budgetLkr',
      headerName: 'Budget',
      type: 'number',
      width: 140,
      sortable: false,
      valueFormatter: (value: number) => formatLkr(value),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 250,
      renderCell: ({ row }) => (
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', height: '100%' }}>
          <RequestStatusChip status={row.status} />
          <CancellationFlags isLateCancellation={row.isLateCancellation} cancelledByOfficer={row.cancelledByOfficer} />
        </Stack>
      ),
    },
    {
      field: 'createdAt',
      headerName: 'Submitted',
      width: 180,
      valueFormatter: (value: string) => formatDateTime(value),
    },
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 80,
      getActions: ({ row }) => [
        <GridActionsCellItem
          key="view"
          icon={<VisibilityIcon />}
          label={`View ${row.purpose}`}
          onClick={(e) => {
            // The row click would open it a second time.
            e.stopPropagation()
            openRequest(row)
          }}
        />,
      ],
    },
  ]

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Booking requests
      </Typography>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup
          exclusive
          size="small"
          aria-label="Status group"
          // An exact status choice overrides the group, so no group shows as selected then.
          value={exactStatuses.length ? null : group}
          onChange={(_e, value: StatusGroup | null) => {
            if (value !== null) setGroup(value)
          }}
        >
          {STATUS_GROUP_KEYS.map((key) => (
            <ToggleButton key={key} value={key}>
              {STATUS_GROUPS[key].label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
        <FormControl size="small" sx={{ minWidth: 220 }}>
          <InputLabel id="request-status-filter-label">Exact status</InputLabel>
          <Select
            labelId="request-status-filter-label"
            label="Exact status"
            multiple
            value={exactStatuses}
            onChange={(e) =>
              setExactStatuses(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)
            }
            renderValue={(selected) => selected.map(requestStatusLabel).join(', ')}
          >
            {REQUEST_STATUSES.map((s) => (
              <MenuItem key={s} value={s}>
                {requestStatusLabel(s)}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <TextField
          label="Search"
          placeholder="Purpose, requester or club"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 260 }}
          slotProps={{ htmlInput: { 'aria-label': 'Purpose, requester or club' } }}
        />
        <TextField
          label="From"
          type="date"
          size="small"
          value={from ?? ''}
          onChange={(e) => setParam('from', e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          label="To"
          type="date"
          size="small"
          value={to ?? ''}
          onChange={(e) => setParam('to', e.target.value)}
          error={rangeInvalid}
          helperText={rangeInvalid ? 'To must be on or after From' : undefined}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: from } }}
        />
        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel id="request-club-filter-label">Club</InputLabel>
          <Select
            labelId="request-club-filter-label"
            label="Club"
            value={clubId !== undefined ? String(clubId) : ''}
            onChange={(e) => setParam('clubId', e.target.value)}
          >
            <MenuItem value="">All clubs</MenuItem>
            {clubOptions.map((c) => (
              <MenuItem key={c.id} value={String(c.id)}>
                {c.name}
              </MenuItem>
            ))}
            {clubIdMissing && <MenuItem value={String(clubId)}>Club #{clubId}</MenuItem>}
          </Select>
        </FormControl>
      </Stack>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="booking requests"
        emptyText="No requests match these filters"
        rowHeight={60}
        onRowClick={openRequest}
      />
    </>
  )
}
