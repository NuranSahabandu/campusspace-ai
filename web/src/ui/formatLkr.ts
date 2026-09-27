// A fixed locale for the grouping and a literal prefix, so the text does not depend on the runtime's ICU data.
const lkrFormat = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** An LKR amount (numeric(10,2) on the server) for display, for example "LKR 1,500.00". */
export const formatLkr = (amount: number): string => `LKR ${lkrFormat.format(amount)}`
