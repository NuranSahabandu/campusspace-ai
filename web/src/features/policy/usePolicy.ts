import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { MAIN_QUERY_META } from '../../api/forbidden'
import type { PolicySettingDto } from '../../api/types'

export const policyKeys = {
  all: ['policy-settings'] as const,
}

/** Every booking-policy setting with its description and who changed it last (Facilities Officer only). */
export function usePolicySettings() {
  return useQuery({
    queryKey: policyKeys.all,
    queryFn: async ({ signal }) => (await api.get<PolicySettingDto[]>('/api/policy-settings', { signal })).data,
    meta: MAIN_QUERY_META,
  })
}
