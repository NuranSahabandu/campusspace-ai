import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import type { QuotationDto } from '../../api/types'
import { formatLkr } from '../../ui/formatLkr'

const qty = (value: number) => value.toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 })

/**
 * A .NET quotation (GET /api/booking-requests/{id}/quotation): the only prices shown anywhere, never the agent's own
 * numbers. An exempt quote keeps every line priced and shows the exemption as a discount.
 */
export function QuoteTable({ quote }: { quote: QuotationDto }) {
  return (
    <TableContainer>
      <Table size="small" aria-label="Quotation">
        <TableHead>
          <TableRow>
            <TableCell>Item</TableCell>
            <TableCell align="right">Qty</TableCell>
            <TableCell align="right">Unit price</TableCell>
            <TableCell align="right">Amount</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {quote.lines.map((line, i) => (
            <TableRow key={`${line.description}-${i}`}>
              <TableCell>{line.description}</TableCell>
              <TableCell align="right">{qty(line.qty)}</TableCell>
              <TableCell align="right">{formatLkr(line.unitPrice)}</TableCell>
              <TableCell align="right">{formatLkr(line.lineTotal)}</TableCell>
            </TableRow>
          ))}
          <TableRow>
            <TableCell colSpan={3}>Subtotal</TableCell>
            <TableCell align="right">{formatLkr(quote.subtotal)}</TableCell>
          </TableRow>
          {quote.discount > 0 && (
            <TableRow>
              <TableCell colSpan={3}>Discount{quote.discountReason ? ` (${quote.discountReason})` : ''}</TableCell>
              <TableCell align="right">−{formatLkr(quote.discount)}</TableCell>
            </TableRow>
          )}
          <TableRow>
            <TableCell colSpan={3}>
              <Typography component="span" sx={{ fontWeight: 600 }}>
                Total{quote.exempt ? ' (fee-exempt)' : ''}
              </Typography>
            </TableCell>
            <TableCell align="right">
              <Typography component="span" sx={{ fontWeight: 600 }} data-testid="quote-total">
                {formatLkr(quote.total)}
              </Typography>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      {quote.status && (
        <Typography variant="caption" color="text.secondary">
          {quote.status === 'Issued' ? 'Issued quotation' : 'Draft quotation (issued on approval)'}
        </Typography>
      )}
    </TableContainer>
  )
}
