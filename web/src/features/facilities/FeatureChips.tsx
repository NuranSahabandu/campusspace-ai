import { Chip, Stack } from '@mui/material'
import type { FeatureRefDto } from '../../api/types'

/** A room's features as small chips showing the code (the name is the tooltip). */
export function FeatureChips({ features }: { features: FeatureRefDto[] }) {
  return (
    <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', height: '100%' }}>
      {features.map((f) => (
        <Chip key={f.code} size="small" variant="outlined" label={f.code} title={f.name} />
      ))}
    </Stack>
  )
}
