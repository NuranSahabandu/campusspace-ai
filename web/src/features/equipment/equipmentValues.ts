// Mirrors EquipmentItemStatuses and EquipmentConditions in backend/CampusSpace.Api/Models. Keep them in sync.
export const EQUIPMENT_STATUSES = ['Available', 'OnLoan', 'UnderRepair', 'Retired'] as const
/** Statuses an officer may set. OnLoan belongs to loans (Phase 2). */
export const EDITABLE_STATUSES = ['Available', 'UnderRepair', 'Retired'] as const
export const EQUIPMENT_CONDITIONS = ['Good', 'MinorWear', 'Damaged'] as const

type ChipColor = 'default' | 'success' | 'info' | 'warning' | 'error'

const LABELS: Record<string, string> = {
  OnLoan: 'On loan',
  UnderRepair: 'Under repair',
  MinorWear: 'Minor wear',
}
/** "UnderRepair" → "Under repair". Unknown values are shown as they are. */
export const equipmentValueLabel = (value: string) => LABELS[value] ?? value

const STATUS_COLORS: Record<string, ChipColor> = {
  Available: 'success',
  OnLoan: 'info',
  UnderRepair: 'warning',
  Retired: 'default',
}
const CONDITION_COLORS: Record<string, ChipColor> = { Good: 'default', MinorWear: 'warning', Damaged: 'error' }

export const statusColor = (status: string): ChipColor => STATUS_COLORS[status] ?? 'default'
export const conditionColor = (condition: string): ChipColor => CONDITION_COLORS[condition] ?? 'default'

/** Same text as EquipmentItemService, so client and server errors read the same. */
export const DAMAGED_MESSAGE = 'Damaged items must be UnderRepair or Retired.'

/** Toast for deleting a type that still has items (409 "In use"). */
export const TYPE_IN_USE_MESSAGE = 'This type still has items. Retire the items instead.'
