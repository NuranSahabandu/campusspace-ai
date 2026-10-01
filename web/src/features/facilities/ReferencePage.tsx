import { type ReactNode, useState } from 'react'
import AddIcon from '@mui/icons-material/Add'
import {
  Button,
  Chip,
  LinearProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import type { UseQueryResult } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { BuildingDto, FeatureDto } from '../../api/types'
import { useApiMutation } from '../../api/useApiMutation'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { QueryErrorAlert } from '../../ui/QueryErrorAlert'
import { equipmentTypesKeys } from '../equipment/useEquipment'
import { BuildingFormDialog } from './BuildingFormDialog'
import { FEATURE_CODE_HINT, FeatureFormDialog } from './FeatureFormDialog'
import { IN_USE_MESSAGE, buildingsKeys, featuresKeys, roomsKeys, useBuildings, useFeatures } from './useFacilities'

interface Column<T> {
  header: string
  cell: (row: T) => ReactNode
}

/** A small unpaged reference list with New / Edit / Delete (§12). The lists are short, so no DataGrid. */
function ReferenceSection<T extends { id: number; code: string }>({
  title,
  noun,
  subtitle,
  query,
  columns,
  onNew,
  onEdit,
  onDelete,
}: {
  title: string
  noun: string
  subtitle?: string
  query: UseQueryResult<T[]>
  columns: Column<T>[]
  onNew: () => void
  onEdit: (row: T) => void
  onDelete: (row: T) => void
}) {
  const { data, isPending, isError, error, refetch } = query

  return (
    <Stack component="section" aria-label={title} spacing={1} sx={{ mb: 4 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <div>
          <Typography variant="h5" component="h2">
            {title}
          </Typography>
          {subtitle && (
            <Typography variant="body2" color="text.secondary">
              {subtitle}
            </Typography>
          )}
        </div>
        <Button variant="contained" startIcon={<AddIcon />} onClick={onNew}>
          New {noun}
        </Button>
      </Stack>
      {isPending && <LinearProgress aria-label={`Loading ${title.toLowerCase()}`} />}
      {isError && (
        <QueryErrorAlert error={error} what={title.toLowerCase()} onRetry={() => refetch()} />
      )}
      {data && (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small" aria-label={title}>
            <TableHead>
              <TableRow>
                {columns.map((c) => (
                  <TableCell key={c.header}>{c.header}</TableCell>
                ))}
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={columns.length + 1}>
                    <Typography color="text.secondary">No {title.toLowerCase()} yet</Typography>
                  </TableCell>
                </TableRow>
              )}
              {data.map((row) => (
                <TableRow key={row.id}>
                  {columns.map((c) => (
                    <TableCell key={c.header}>{c.cell(row)}</TableCell>
                  ))}
                  <TableCell align="right">
                    <Button size="small" aria-label={`Edit ${row.code}`} onClick={() => onEdit(row)}>
                      Edit
                    </Button>
                    <Button size="small" color="error" aria-label={`Delete ${row.code}`} onClick={() => onDelete(row)}>
                      Delete
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </Stack>
  )
}

/** Buildings and room features (UC13 reference data), Facilities Officer only. */
export function ReferencePage() {
  const buildings = useBuildings()
  const features = useFeatures()
  const [buildingDialog, setBuildingDialog] = useState<{ building?: BuildingDto } | null>(null)
  const [featureDialog, setFeatureDialog] = useState<{ feature?: FeatureDto } | null>(null)
  const [deletingBuilding, setDeletingBuilding] = useState<BuildingDto | null>(null)
  const [deletingFeature, setDeletingFeature] = useState<FeatureDto | null>(null)

  // Close the confirm dialog either way; a 409 "In use" becomes the IN_USE_MESSAGE toast.
  const deleteBuilding = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/buildings/${id}`),
    invalidate: [buildingsKeys.all, roomsKeys.all],
    successMessage: 'Building deleted',
    conflictMessage: IN_USE_MESSAGE,
  })
  const deleteFeature = useApiMutation<number>({
    mutationFn: (id) => api.delete(`/api/features/${id}`),
    invalidate: [featuresKeys.all, roomsKeys.all, equipmentTypesKeys.all],
    successMessage: 'Feature deleted',
    conflictMessage: IN_USE_MESSAGE,
  })

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 2 }}>
        Buildings &amp; features
      </Typography>

      <ReferenceSection<BuildingDto>
        title="Buildings"
        noun="building"
        query={buildings}
        columns={[
          { header: 'Code', cell: (b) => b.code },
          { header: 'Name', cell: (b) => b.name },
          {
            header: 'Status',
            cell: (b) => (
              <Chip size="small" label={b.isActive ? 'Active' : 'Inactive'} color={b.isActive ? 'success' : 'default'} />
            ),
          },
        ]}
        onNew={() => setBuildingDialog({})}
        onEdit={(building) => setBuildingDialog({ building })}
        onDelete={setDeletingBuilding}
      />

      <ReferenceSection<FeatureDto>
        title="Features"
        noun="feature"
        subtitle={`Codes are ${FEATURE_CODE_HINT}.`}
        query={features}
        columns={[
          { header: 'Code', cell: (f) => f.code },
          { header: 'Name', cell: (f) => f.name },
        ]}
        onNew={() => setFeatureDialog({})}
        onEdit={(feature) => setFeatureDialog({ feature })}
        onDelete={setDeletingFeature}
      />

      {buildingDialog && (
        <BuildingFormDialog building={buildingDialog.building} onClose={() => setBuildingDialog(null)} />
      )}
      {featureDialog && <FeatureFormDialog feature={featureDialog.feature} onClose={() => setFeatureDialog(null)} />}
      <ConfirmDialog
        open={deletingBuilding !== null}
        title="Delete building?"
        message={`Delete ${deletingBuilding?.code ?? ''}? A building that still has rooms cannot be deleted; deactivate it instead.`}
        confirmLabel="Delete"
        destructive
        pending={deleteBuilding.isPending}
        onConfirm={() => deletingBuilding && deleteBuilding.mutate(deletingBuilding.id, { onSettled: () => setDeletingBuilding(null) })}
        onClose={() => setDeletingBuilding(null)}
      />
      <ConfirmDialog
        open={deletingFeature !== null}
        title="Delete feature?"
        message={`Delete ${deletingFeature?.code ?? ''}? A feature that rooms still have cannot be deleted.`}
        confirmLabel="Delete"
        destructive
        pending={deleteFeature.isPending}
        onConfirm={() => deletingFeature && deleteFeature.mutate(deletingFeature.id, { onSettled: () => setDeletingFeature(null) })}
        onClose={() => setDeletingFeature(null)}
      />
    </>
  )
}
