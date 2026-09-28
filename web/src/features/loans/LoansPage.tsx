import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { Box, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import type { LoanDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { formatDateTime } from '../../ui/formatDateTime'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { LoanDetailDrawer } from './LoanDetailDrawer'
import { LoanFlags } from './LoanFlags'
import { LOANS_SORT_FIELDS, type LoansParams, useLoans } from './useLoans'

/** The quick filter: the API's ?overdue= (absent, true or false). */
const FILTERS = {
  all: { label: 'All', overdue: undefined },
  overdue: { label: 'Overdue', overdue: true },
  notOverdue: { label: 'Returned or not due', overdue: false },
} as const

type Filter = keyof typeof FILTERS

// ?overdue=true|false in the URL; anything else is ignored (All).
const filterOf = (raw: string | null): Filter => (raw === 'true' ? 'overdue' : raw === 'false' ? 'notOverdue' : 'all')

/** Two lines in one cell: a main value and a smaller one under it. */
function TwoLines({ primary, secondary }: { primary: string; secondary?: string }) {
  return (
    <Box sx={{ lineHeight: 1.3, py: 0.5 }}>
      <Typography variant="body2">{primary}</Typography>
      {secondary && (
        <Typography variant="caption" color="text.secondary">
          {secondary}
        </Typography>
      )}
    </Box>
  )
}

/**
 * Equipment loans for a Facilities Officer (§12, Component B, UC09–UC12), read-only: technicians hand items over and
 * take them back on mobile. The filter, search, sort and page live in the URL. A row opens its details in a drawer.
 */
export function LoansPage() {
  const table = useServerTable({ sortFields: LOANS_SORT_FIELDS, initialSort: [{ field: 'dueAt', sort: 'desc' }], urlState: true })
  const [searchParams] = useSearchParams()
  const filter = filterOf(searchParams.get('overdue'))
  const params: LoansParams = { ...table.params, overdue: FILTERS[filter].overdue }
  const query = useLoans(params)
  const [openId, setOpenId] = useState<number | null>(null)

  const setFilter = (value: Filter) =>
    table.updateUrl((p) => {
      const overdue = FILTERS[value].overdue
      if (overdue === undefined) p.delete('overdue')
      else p.set('overdue', String(overdue))
    })

  const columns: GridColDef<LoanDto>[] = [
    { field: 'assetTag', headerName: 'Asset tag', width: 140, sortable: false },
    { field: 'typeCode', headerName: 'Type', width: 150, sortable: false },
    { field: 'roomCode', headerName: 'Room', width: 100, sortable: false },
    {
      field: 'checkedOutAt',
      headerName: 'Checked out',
      width: 200,
      sortable: false,
      renderCell: ({ row }) => <TwoLines primary={formatDateTime(row.checkedOutAt)} secondary={row.checkedOutByName} />,
    },
    { field: 'dueAt', headerName: 'Due', width: 190, valueFormatter: (value: string) => formatDateTime(value) },
    {
      field: 'checkedInAt',
      headerName: 'Returned',
      width: 200,
      sortable: false,
      renderCell: ({ row }) =>
        row.checkedInAt ? (
          <TwoLines primary={formatDateTime(row.checkedInAt)} secondary={row.returnCondition ?? undefined} />
        ) : (
          <TwoLines primary="Out" />
        ),
    },
    {
      field: 'flags',
      headerName: 'Flags',
      minWidth: 220,
      flex: 1,
      sortable: false,
      renderCell: ({ row }) => <LoanFlags loan={row} />,
    },
  ]

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Loans
      </Typography>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup
          exclusive
          size="small"
          aria-label="Loan filter"
          value={filter}
          onChange={(_e, value: Filter | null) => {
            if (value !== null) setFilter(value)
          }}
        >
          {(Object.keys(FILTERS) as Filter[]).map((key) => (
            <ToggleButton key={key} value={key}>
              {FILTERS[key].label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
        <TextField
          label="Search"
          placeholder="Asset tag or type code"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 260 }}
          slotProps={{ htmlInput: { 'aria-label': 'Asset tag or type code' } }}
        />
      </Stack>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="loans"
        emptyText={filter === 'overdue' ? 'No overdue loans' : 'No loans'}
        rowHeight={60}
        onRowClick={(row) => setOpenId(row.id)}
      />

      {openId !== null && <LoanDetailDrawer id={openId} onClose={() => setOpenId(null)} />}
    </>
  )
}
