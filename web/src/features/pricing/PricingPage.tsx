import { type ReactNode, useState } from 'react'
import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import {
  Button,
  Chip,
  FormControl,
  FormControlLabel,
  IconButton,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Switch,
  Tooltip,
  Typography,
} from '@mui/material'
import type { GridColDef } from '@mui/x-data-grid'
import { api } from '../../api/client'
import type { PricingRuleDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { useServerTable } from '../../hooks/useServerTable'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { formatDateOnly } from '../../ui/formatDateTime'
import { CurrentPricesCard } from './CurrentPricesCard'
import { PricingRuleFormDialog } from './PricingRuleFormDialog'
import {
  formatRate,
  IN_EFFECT_TOOLTIP,
  PRICING_EXPLAINER,
  PRICING_STATUSES,
  pricingStatusColor,
  REQUESTER_ROLES,
  ROOM_TYPES,
  roomTypeLabel,
} from './pricingValues'
import { PRICING_SORT_FIELDS, pricingKeys, usePricingRules } from './usePricing'

const ruleName = (r: PricingRuleDto) => `${r.roomType} ${r.requesterRole} from ${formatDateOnly(r.validFrom)}`

/** Edit or Delete for one rule. Rules already in effect are read-only, so their buttons are disabled with the reason. */
function RuleAction({ rule, label, icon, onClick }: { rule: PricingRuleDto; label: string; icon: ReactNode; onClick: () => void }) {
  const scheduled = rule.status === PRICING_STATUSES.Scheduled
  const button = (
    <IconButton size="small" aria-label={`${label} ${ruleName(rule)}`} disabled={!scheduled} onClick={onClick}>
      {icon}
    </IconButton>
  )
  return scheduled ? (
    button
  ) : (
    <Tooltip title={IN_EFFECT_TOOLTIP}>
      {/* A disabled button fires no events, so the span carries the tooltip. */}
      <span>{button}</span>
    </Tooltip>
  )
}

/** Facilities Officer pricing (§5 Component D, UC16): the prices in effect and the rule history. */
export function PricingPage() {
  const table = useServerTable({
    sortFields: PRICING_SORT_FIELDS,
    initialSort: [{ field: 'roomType', sort: 'asc' }],
  })
  const [roomType, setRoomType] = useState('')
  const [requesterRole, setRequesterRole] = useState('')
  const [includeHistory, setIncludeHistory] = useState(false)
  const query = usePricingRules({
    ...table.params,
    roomType: roomType || undefined,
    requesterRole: requesterRole || undefined,
    includeHistory: includeHistory || undefined,
  })
  const [dialog, setDialog] = useState<{ rule?: PricingRuleDto } | null>(null)
  const [deleting, setDeleting] = useState<PricingRuleDto | null>(null)

  const remove = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/pricing-rules/${id}`),
    invalidate: [pricingKeys.all],
    successMessage: 'Pricing rule deleted',
  })

  const filtered = table.search.trim() !== '' || roomType !== '' || requesterRole !== ''

  const columns: GridColDef<PricingRuleDto>[] = [
    { field: 'roomType', headerName: 'Room type', flex: 1, minWidth: 140, valueFormatter: (v: string) => roomTypeLabel(v) },
    { field: 'requesterRole', headerName: 'Role', width: 120 },
    {
      field: 'hourlyRate',
      headerName: 'Rate',
      width: 170,
      valueGetter: (_v, row) => formatRate(row),
    },
    { field: 'validFrom', headerName: 'Valid from', width: 130, valueFormatter: (v: string) => formatDateOnly(v) },
    {
      field: 'status',
      headerName: 'Status',
      width: 130,
      sortable: false,
      renderCell: ({ row }) => <Chip label={row.status} color={pricingStatusColor(row.status)} size="small" />,
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 110,
      sortable: false,
      renderCell: ({ row }) => (
        <Stack direction="row" sx={{ alignItems: 'center', height: '100%' }}>
          <RuleAction rule={row} label="Edit" icon={<EditIcon fontSize="small" />} onClick={() => setDialog({ rule: row })} />
          <RuleAction rule={row} label="Delete" icon={<DeleteIcon fontSize="small" />} onClick={() => setDeleting(row)} />
        </Stack>
      ),
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Pricing
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialog({})}>
          New rule
        </Button>
      </Stack>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        {PRICING_EXPLAINER}
      </Typography>

      <CurrentPricesCard />

      <Typography variant="h6" component="h2" sx={{ mb: 1 }}>
        Rules
      </Typography>
      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="pricing-room-type-filter-label">Room type</InputLabel>
          <Select
            labelId="pricing-room-type-filter-label"
            label="Room type"
            value={roomType}
            onChange={(e) => {
              setRoomType(e.target.value)
              table.resetPage()
            }}
          >
            <MenuItem value="">All room types</MenuItem>
            {ROOM_TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {roomTypeLabel(t)}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="pricing-role-filter-label">Requester role</InputLabel>
          <Select
            labelId="pricing-role-filter-label"
            label="Requester role"
            value={requesterRole}
            onChange={(e) => {
              setRequesterRole(e.target.value)
              table.resetPage()
            }}
          >
            <MenuItem value="">All roles</MenuItem>
            {REQUESTER_ROLES.map((r) => (
              <MenuItem key={r} value={r}>
                {r}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControlLabel
          label="Show superseded"
          control={
            <Switch
              checked={includeHistory}
              onChange={(e) => {
                setIncludeHistory(e.target.checked)
                table.resetPage()
              }}
            />
          }
        />
      </Stack>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="pricing rules"
        emptyText={filtered ? 'No pricing rules match' : 'No pricing rules yet'}
      />

      {dialog && <PricingRuleFormDialog rule={dialog.rule} onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={deleting !== null}
        title="Delete pricing rule?"
        message={deleting ? `Delete the ${formatRate(deleting)} rule for ${ruleName(deleting)}? It has not started yet.` : ''}
        confirmLabel="Delete"
        destructive
        pending={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id, { onSettled: () => setDeleting(null) })}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}
