import CancelIcon from '@mui/icons-material/Cancel'
import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { Accordion, AccordionDetails, AccordionSummary, List, ListItem, ListItemIcon, ListItemText, Typography } from '@mui/material'
import type { ValidationAttemptDto } from '../../api/types'

function Rules({ attempt, label }: { attempt: ValidationAttemptDto; label: string }) {
  return (
    <List dense disablePadding aria-label={label}>
      {attempt.rules.map((r) => (
        <ListItem key={r.rule} disableGutters data-passed={r.passed}>
          <ListItemIcon sx={{ minWidth: 32 }}>
            {r.passed ? (
              <CheckCircleIcon color="success" titleAccess="Passed" />
            ) : (
              <CancelIcon color="error" titleAccess="Failed" />
            )}
          </ListItemIcon>
          <ListItemText
            primary={r.rule}
            secondary={r.message}
            slotProps={{ primary: { sx: { color: r.passed ? 'success.main' : 'error.main', fontWeight: 600 } } }}
          />
        </ListItem>
      ))}
    </List>
  )
}

/**
 * The validator's checklist (V01–V12): the latest attempt as green/red rows with the validator's messages (which show
 * the policy values it used); earlier attempts are collapsed.
 */
export function ValidationChecklist({ validation }: { validation: ValidationAttemptDto[] }) {
  const [latest, ...earlier] = validation
  if (!latest) return <Typography color="text.secondary">No validation results yet</Typography>
  const failed = latest.rules.filter((r) => !r.passed).length
  return (
    <>
      <Typography variant="body2" sx={{ mb: 1 }}>
        Attempt {latest.attempt}: {failed === 0 ? `all ${latest.rules.length} rules passed` : `${failed} of ${latest.rules.length} rules failed`}
      </Typography>
      <Rules attempt={latest} label="Validation checklist" />
      {earlier.length > 0 && (
        <Accordion disableGutters variant="outlined" sx={{ mt: 1 }}>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="body2">Earlier attempts ({earlier.length})</Typography>
          </AccordionSummary>
          <AccordionDetails>
            {earlier.map((a) => (
              <div key={a.attempt}>
                <Typography variant="subtitle2">Attempt {a.attempt}</Typography>
                <Rules attempt={a} label={`Validation attempt ${a.attempt}`} />
              </div>
            ))}
          </AccordionDetails>
        </Accordion>
      )}
    </>
  )
}
