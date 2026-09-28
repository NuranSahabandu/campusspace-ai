import type { ReactNode } from 'react'
import CloseIcon from '@mui/icons-material/Close'
import { Alert, Box, Button, Drawer, IconButton, LinearProgress, Skeleton, Stack, Typography } from '@mui/material'
import { parseProblem } from '../../api/problem'
import type { LoanDto } from '../../api/types'
import { formatDateTime } from '../../ui/formatDateTime'
import { LoanFlags } from './LoanFlags'
import { useBlobImageRef, useLoan, useLoanPhoto } from './useLoans'

/** One loan's details (GET /api/loans/{id}) in a right-hand drawer, with the damage note and photo. */
export function LoanDetailDrawer({ id, onClose }: { id: number; onClose: () => void }) {
  const { data: loan, isPending, isError, error, refetch } = useLoan(id)

  return (
    <Drawer anchor="right" open onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 420 } } } }}>
      <Box role="region" aria-label="Loan details" sx={{ p: 2 }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h5" component="h2">
            {loan ? `Loan of ${loan.assetTag}` : 'Loan'}
          </Typography>
          <IconButton aria-label="Close" onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>
        {isPending ? (
          <LinearProgress aria-label="Loading loan" />
        ) : isError ? (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => refetch()}>
                Retry
              </Button>
            }
          >
            Could not load the loan: {parseProblem(error).title}
          </Alert>
        ) : (
          <LoanDetails loan={loan} />
        )}
      </Box>
    </Drawer>
  )
}

function LoanDetails({ loan }: { loan: LoanDto }) {
  return (
    <Stack spacing={1.5}>
      <LoanFlags loan={loan} />
      <Fact label="Item">
        {loan.assetTag} · {loan.typeCode}
      </Fact>
      <Fact label="Booking">
        #{loan.bookingId} · {loan.roomCode}
      </Fact>
      <Fact label="Checked out">
        {formatDateTime(loan.checkedOutAt)} by {loan.checkedOutByName}
      </Fact>
      <Fact label="Due">{formatDateTime(loan.dueAt)}</Fact>
      <Fact label="Returned">
        {loan.checkedInAt ? `${formatDateTime(loan.checkedInAt)} by ${loan.checkedInByName ?? 'Unknown user'}` : 'Not yet'}
      </Fact>
      {loan.returnCondition && <Fact label="Condition">{loan.returnCondition}</Fact>}
      {loan.damageNote && (
        <Fact label="Damage note">
          {/* Untrusted technician text: a plain React text child, line breaks kept. */}
          <Box component="span" data-testid="damage-note" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {loan.damageNote}
          </Box>
        </Fact>
      )}
      {loan.hasPhoto && <LoanPhoto loan={loan} />}
    </Stack>
  )
}

/**
 * The damage photo, fetched through the authenticated client as a Blob and shown from an object URL that is revoked
 * when the drawer closes. Never a bare <img src> to the API: that request would have no Bearer token.
 */
function LoanPhoto({ loan }: { loan: LoanDto }) {
  const { data: blob, isError, error, refetch } = useLoanPhoto(loan.id, true)
  const imgRef = useBlobImageRef(blob)

  return (
    <div>
      <Typography variant="caption" color="text.secondary">
        Damage photo
      </Typography>
      {isError ? (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => refetch()}>
              Retry
            </Button>
          }
        >
          Could not load the photo: {parseProblem(error).title}
        </Alert>
      ) : blob ? (
        <Box
          component="img"
          ref={imgRef}
          alt={`Damage photo for ${loan.assetTag}`}
          sx={{ display: 'block', width: '100%', borderRadius: 1, border: 1, borderColor: 'divider' }}
        />
      ) : (
        <Skeleton variant="rectangular" height={240} aria-label="Loading photo" />
      )}
    </div>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography>{children}</Typography>
    </div>
  )
}
