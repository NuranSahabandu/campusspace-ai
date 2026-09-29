import { useNavigate } from 'react-router'
import { Box, Stack, TextField, Typography } from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import type { ApprovalQueueItemDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { formatCampusTimeRange, formatDateTime } from '../../ui/formatDateTime'
import { formatLkr } from '../../ui/formatLkr'
import { APPROVAL_QUEUE_SORT_FIELDS, useApprovalQueue } from './useApprovals'

const quoteText = (row: ApprovalQueueItemDto) =>
  row.exempt ? 'Fee-exempt' : row.draftTotal === null ? '—' : formatLkr(row.draftTotal)

/**
 * The Facilities Officer's approval queue (UC17, plan §12): requests waiting for a decision, oldest pending first,
 * re-fetched every 15 s. Search, sort and page live in the URL, so coming back from a request restores the list.
 */
export function ApprovalQueuePage() {
  const table = useServerTable({
    sortFields: APPROVAL_QUEUE_SORT_FIELDS,
    initialSort: [{ field: 'pendingSince', sort: 'asc' }],
    urlState: true,
  })
  const query = useApprovalQueue(table.params)
  const navigate = useNavigate()

  const columns: GridColDef<ApprovalQueueItemDto>[] = [
    {
      field: 'purpose',
      headerName: 'Purpose',
      flex: 1.3,
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
      minWidth: 180,
      sortable: false,
      renderCell: ({ row }) => (
        <Stack sx={{ justifyContent: 'center', height: '100%', lineHeight: 1.3, minWidth: 0 }}>
          <Typography variant="body2" noWrap>
            {row.requesterName}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap>
            {row.requesterRole}
            {row.clubName ? ` · ${row.clubName}` : ' · Academic'}
          </Typography>
        </Stack>
      ),
    },
    {
      field: 'when',
      headerName: 'When',
      width: 250,
      valueGetter: (_value, row) => formatCampusTimeRange(row.start, row.end),
    },
    { field: 'attendees', headerName: 'Attendees', type: 'number', width: 110 },
    {
      field: 'proposedRoomCode',
      headerName: 'Room',
      width: 100,
      sortable: false,
      valueGetter: (value: string | null) => value ?? '—',
    },
    {
      field: 'draftTotal',
      headerName: 'Quote',
      width: 140,
      sortable: false,
      align: 'right',
      headerAlign: 'right',
      valueGetter: (_value, row) => quoteText(row),
    },
    {
      field: 'revisionNo',
      headerName: 'Revision',
      type: 'number',
      width: 100,
      sortable: false,
      valueGetter: (value: number | null) => value ?? '—',
    },
    {
      field: 'pendingSince',
      headerName: 'Pending since',
      width: 180,
      valueFormatter: (value: string) => formatDateTime(value),
    },
  ]

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Approvals
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        Proposals waiting for your decision, oldest first. The list updates every 15 seconds.
      </Typography>
      <TextField
        label="Search"
        placeholder="Purpose, requester or club"
        value={table.search}
        onChange={(e) => table.setSearch(e.target.value)}
        size="small"
        sx={{ minWidth: 260, mb: 2 }}
        slotProps={{ htmlInput: { 'aria-label': 'Purpose, requester or club' } }}
      />
      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="pending approvals"
        emptyText="No pending approvals"
        getRowId={(row) => row.requestId}
        rowHeight={60}
        onRowClick={(row) => navigate(`/approvals/${row.requestId}`)}
      />
    </>
  )
}
