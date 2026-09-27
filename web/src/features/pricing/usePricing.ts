import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { type ListParams, usePagedQuery } from '../../api/list'
import type { CurrentPricingRuleDto, PricingRuleDto } from '../../api/types'

export interface PricingRulesParams extends ListParams {
  roomType?: string
  requesterRole?: string
  /** Sent only when true (compactParams drops undefined). */
  includeHistory?: true
}

// Every rule write changes both the list and the current prices, so writes invalidate pricingKeys.all.
export const pricingKeys = {
  all: ['pricing-rules'] as const,
  list: (params: PricingRulesParams) => [...pricingKeys.all, 'list', params] as const,
  current: () => [...pricingKeys.all, 'current'] as const,
}

/** Grid column → PricingRulesQuery.SortFields. */
export const PRICING_SORT_FIELDS: Record<string, string> = {
  roomType: 'roomType',
  requesterRole: 'requesterRole',
  validFrom: 'validFrom',
  hourlyRate: 'hourlyRate',
}

export function usePricingRules(params: PricingRulesParams) {
  return usePagedQuery<PricingRuleDto>(pricingKeys.list(params), '/api/pricing-rules', params)
}

/** One row per room type and requester role; the rule fields are null where no rule is in effect. */
export function useCurrentPrices() {
  return useQuery({
    queryKey: pricingKeys.current(),
    queryFn: async ({ signal }) => (await api.get<CurrentPricingRuleDto[]>('/api/pricing-rules/current', { signal })).data,
  })
}
