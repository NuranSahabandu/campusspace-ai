const integer = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })
const oneDecimal = new Intl.NumberFormat('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

/** A duration in ms: "850 ms" below a second, else seconds with one decimal ("12.4 s"); "—" when there is none. */
export const formatMs = (value: number | null | undefined): string => {
  if (value === null || value === undefined) return '—'
  return value < 1000 ? `${integer.format(value)} ms` : `${oneDecimal.format(value / 1000)} s`
}

/** A token count with thousands separators; "—" when there is none. */
export const formatTokens = (value: number | null | undefined): string =>
  value === null || value === undefined ? '—' : integer.format(value)

/** A server rate (0–1) as a percentage with one decimal, or "—" when the server sent null (denominator 0). */
export const formatPercent = (rate: number | null): string =>
  rate === null ? '—' : `${oneDecimal.format(rate * 100)}%`

/**
 * A rate with its denominator, always: "75.0% (15 of 20)". With a denominator of 0 the server's rate is null and this
 * shows "— (0 of 0)", never 0%.
 */
export const formatRate = (rate: number | null, numerator: number, denominator: number): string =>
  `${denominator === 0 ? '—' : formatPercent(rate)} (${integer.format(numerator)} of ${integer.format(denominator)})`
