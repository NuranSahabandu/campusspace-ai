import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import SwapHorizIcon from '@mui/icons-material/SwapHoriz'
import { Button, FormControl, InputLabel, Link, MenuItem, Select, Stack, TextField, Typography } from '@mui/material'
import { GridActionsCellItem, type GridColDef } from '@mui/x-data-grid'
import { api } from '../../api/client'
import type { EquipmentTypeDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { useServerTable } from '../../hooks/useServerTable'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { ServerDataGrid } from '../../ui/ServerDataGrid'
import { formatLkr } from '../../ui/formatLkr'
import { EquipmentTypeFormDialog } from './EquipmentTypeFormDialog'
import { SubstitutesDialog } from './SubstitutesDialog'
import { TYPE_IN_USE_MESSAGE } from './equipmentValues'
import {
  EQUIPMENT_TYPES_SORT_FIELDS,
  equipmentTypesKeys,
  useEquipmentCategories,
  useEquipmentTypes,
} from './useEquipment'

/** Facilities Officer list of equipment types (§12, Component B) with their fees, substitutes and item counts. */
export function EquipmentTypesPage() {
  const table = useServerTable({
    sortFields: EQUIPMENT_TYPES_SORT_FIELDS,
    initialSort: [{ field: 'code', sort: 'asc' }],
  })
  const categories = useEquipmentCategories()
  const [category, setCategory] = useState('')
  const query = useEquipmentTypes({ ...table.params, category: category || undefined })
  const [dialog, setDialog] = useState<{ type?: EquipmentTypeDto } | null>(null)
  const [substitutesOf, setSubstitutesOf] = useState<EquipmentTypeDto | null>(null)
  const [deleting, setDeleting] = useState<EquipmentTypeDto | null>(null)

  const remove = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/equipment-types/${id}`),
    invalidate: [equipmentTypesKeys.all],
    successMessage: 'Equipment type deleted',
    conflictMessage: TYPE_IN_USE_MESSAGE,
  })

  const filtered = table.search.trim() !== '' || category !== ''

  const columns: GridColDef<EquipmentTypeDto>[] = [
    { field: 'code', headerName: 'Code', width: 170 },
    { field: 'name', headerName: 'Name', flex: 1, minWidth: 180 },
    { field: 'category', headerName: 'Category', width: 130 },
    {
      field: 'feePerBooking',
      headerName: 'Fee',
      type: 'number',
      width: 140,
      valueFormatter: (value: number) => formatLkr(value),
    },
    {
      field: 'coveredByFeatureName',
      headerName: 'Covered by',
      width: 170,
      sortable: false,
      valueFormatter: (value: string | null) => value ?? '—',
    },
    {
      field: 'itemCounts',
      headerName: 'Items',
      description: 'Available / total',
      width: 150,
      sortable: false,
      renderCell: ({ row }) => (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', height: '100%' }}>
          <Link component={RouterLink} to={`/equipment/items?typeId=${row.id}`} title={`Items of ${row.code}`}>
            {row.itemCounts.available} / {row.itemCounts.total}
          </Link>
          {row.itemCounts.underRepair > 0 && (
            <Typography variant="caption" color="warning.main">
              {row.itemCounts.underRepair} under repair
            </Typography>
          )}
        </Stack>
      ),
    },
    {
      field: 'actions',
      type: 'actions',
      headerName: 'Actions',
      width: 130,
      getActions: ({ row }) => [
        <GridActionsCellItem key="edit" icon={<EditIcon />} label={`Edit ${row.code}`} onClick={() => setDialog({ type: row })} />,
        <GridActionsCellItem
          key="substitutes"
          icon={<SwapHorizIcon />}
          label={`Substitutes of ${row.code}`}
          onClick={() => setSubstitutesOf(row)}
        />,
        <GridActionsCellItem key="delete" icon={<DeleteIcon />} label={`Delete ${row.code}`} onClick={() => setDeleting(row)} />,
      ],
    },
  ]

  return (
    <>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1">
          Equipment types
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialog({})}>
          New type
        </Button>
      </Stack>

      <Stack direction="row" spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap', alignItems: 'center' }}>
        <TextField
          label="Search code or name"
          value={table.search}
          onChange={(e) => table.setSearch(e.target.value)}
          size="small"
          sx={{ minWidth: 220 }}
        />
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="category-filter-label">Category</InputLabel>
          <Select
            labelId="category-filter-label"
            label="Category"
            value={category}
            onChange={(e) => {
              setCategory(e.target.value)
              table.resetPage()
            }}
          >
            <MenuItem value="">All categories</MenuItem>
            {(categories.data ?? []).map((c) => (
              <MenuItem key={c} value={c}>
                {c}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>

      <ServerDataGrid
        query={query}
        columns={columns}
        gridProps={table.gridProps}
        noun="equipment types"
        emptyText={filtered ? 'No equipment types match' : 'No equipment types yet'}
      />

      {dialog && <EquipmentTypeFormDialog type={dialog.type} onClose={() => setDialog(null)} />}
      {substitutesOf && <SubstitutesDialog type={substitutesOf} onClose={() => setSubstitutesOf(null)} />}
      <ConfirmDialog
        open={deleting !== null}
        title="Delete equipment type?"
        message={`Delete ${deleting?.code ?? ''}? A type that still has items can't be deleted.`}
        confirmLabel="Delete"
        destructive
        pending={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id, { onSettled: () => setDeleting(null) })}
        onClose={() => setDeleting(null)}
      />
    </>
  )
}
