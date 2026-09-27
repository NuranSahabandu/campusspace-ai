import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import AddIcon from '@mui/icons-material/Add'
import EditIcon from '@mui/icons-material/Edit'
import { Box, Button, Chip, FormControl, InputLabel, MenuItem, Select, Stack, TextField, Typography } from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import type { EquipmentItemDto } from '../../api/types'
import { useServerTable } from '../../hooks/useServerTable'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { formatDateTime } from '../../ui/formatDateTime'
import { EquipmentItemFormDialog } from './EquipmentItemFormDialog'
import {
  EQUIPMENT_CONDITIONS,
  EQUIPMENT_STATUSES,
  conditionColor,
  equipmentValueLabel,
  statusColor,
} from './equipmentValues'
import { EQUIPMENT_ITEMS_SORT_FIELDS, useEquipmentItems, useEquipmentTypeOptions } from './useEquipment'

/** ?typeId= as a positive whole number, or undefined when it is missing or not one. */
const parseTypeId = (raw: string | null) => (raw !== null && /^[1-9]\d*$/.test(raw) ? Number(raw) : undefined)

/**
 * Facilities Officer list of equipment items (§12, Component B). The type filter lives in the URL (?typeId=) so the
 * Items link on Equipment types can open this page already filtered.
 */
export function EquipmentItemsPage() {
  const table = useServerTable({
    sortFields: EQUIPMENT_ITEMS_SORT_FIELDS,
    initialSort: [{ field: 'assetTag', sort: 'asc' }],
  })
  const types = useEquipmentTypeOptions()
  const [searchParams, setSearchParams] = useSearchParams()
  const rawTypeId = searchParams.get('typeId')
  const typeId = parseTypeId(rawTypeId)
  const [status, setStatus] = useState('')
  const [condition, setCondition] = useState('')
  const query = useEquipmentItems({
    ...table.params,
    typeId,
    status: status || undefined,
    condition: condition || undefined,
  })
  const [dialog, setDialog] = useState<{ item?: EquipmentItemDto } | null>(null)

  // Drop a typeId that is not a positive whole number, so the URL matches what is shown ("All types").
  useEffect(() => {
    if (rawTypeId !== null && typeId === undefined)
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.delete('typeId')
          return next
        },
        { replace: true },
      )
  }, [rawTypeId, typeId, setSearchParams])

  const setTypeFilter = (value: string) => {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set('typeId', value)
        else next.delete('typeId')
        return next
      },
      { replace: true },
    )
    table.resetPage()
  }

  // Every filter change starts again from the first page.
  const onFilter = (set: (value: string) => void) => (value: string) => {
    set(value)
    table.resetPage()
  }

  const typeOptions = types.data ?? []
  const typeIdMissing = typeId !== undefined && !typeOptions.some((t) => t.id === typeId)

  const columns: GridColDef<EquipmentItemDto>[] = [
    { field: 'assetTag', headerName: 'Asset tag', width: 150 },
    {
      field: 'type',
      headerName: 'Type',
      flex: 1,
      minWidth: 220,
      valueGetter: (_value, row) => `${row.typeCode} — ${row.typeName}`,
    },
    {
      field: 'condition',
      headerName: 'Condition',
      width: 130,
      renderCell: ({ row }) => (
        <Chip size="small" label={equipmentValueLabel(row.condition)} color={conditionColor(row.condition)} />
      ),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 140,
      renderCell: ({ row }) => (
        <Chip size="small" label={equipmentValueLabel(row.status)} color={statusColor(row.status)} />
      ),
    },
    {
      field: 'notes',
      headerName: 'Notes',
      flex: 1,
      minWidth: 180,
      sortable: false,
      renderCell: ({ row }) => (
        <Box
          component="span"
          title={row.notes ?? undefined}
          sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
        >
          {row.notes ?? ''}
        </Box>
      ),
    },
    {
      field: 'updatedAt',
      headerName: 'Updated',
      width: 180,
      valueFormatter: (value: string) => formatDateTime(value),
    },
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 80,
      getActions: ({ row }) => [
        <GridActionsCellItem key="edit" icon={<EditIcon />} label={`Edit ${row.assetTag}`} onClick={() => setDialog({ item: row })} />,
      ],
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Equipment items
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialog({})}>
          New item
        </Button>
      </Stack>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          label="Search asset tag"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 200 }}
        />
        <FormControl size="small" sx={{ minWidth: 240 }}>
          <InputLabel id="item-type-filter-label">Type</InputLabel>
          <Select
            labelId="item-type-filter-label"
            label="Type"
            value={typeId !== undefined ? String(typeId) : ''}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <MenuItem value="">All types</MenuItem>
            {typeOptions.map((t) => (
              <MenuItem key={t.id} value={String(t.id)}>
                {t.code} — {t.name}
              </MenuItem>
            ))}
            {typeIdMissing && <MenuItem value={String(typeId)}>Type #{typeId}</MenuItem>}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="item-status-filter-label">Status</InputLabel>
          <Select
            labelId="item-status-filter-label"
            label="Status"
            value={status}
            onChange={(e) => onFilter(setStatus)(e.target.value)}
          >
            <MenuItem value="">All statuses</MenuItem>
            {EQUIPMENT_STATUSES.map((s) => (
              <MenuItem key={s} value={s}>
                {equipmentValueLabel(s)}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="item-condition-filter-label">Condition</InputLabel>
          <Select
            labelId="item-condition-filter-label"
            label="Condition"
            value={condition}
            onChange={(e) => onFilter(setCondition)(e.target.value)}
          >
            <MenuItem value="">All conditions</MenuItem>
            {EQUIPMENT_CONDITIONS.map((c) => (
              <MenuItem key={c} value={c}>
                {equipmentValueLabel(c)}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="equipment items"
        emptyText="No items match these filters"
      />

      {dialog && <EquipmentItemFormDialog item={dialog.item} onClose={() => setDialog(null)} />}
    </>
  )
}
