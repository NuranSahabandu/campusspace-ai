import { type ListParams, usePagedQuery } from '../../api/list'
import type { AuditLogDto } from '../../api/types'

/** GET /api/audit-logs parameters. from/to are instants with an offset (inclusive). */
export interface AuditLogsParams extends ListParams {
  action?: string
  entityType?: string
  from?: string
  to?: string
}

export const auditLogsKeys = {
  all: ['audit-logs'] as const,
  list: (params: AuditLogsParams) => [...auditLogsKeys.all, 'list', params] as const,
}

// Mirror AuditActions and the IAuditable entity names in the API.
export const AUDIT_ACTIONS = ['Created', 'Updated', 'Deleted', 'Login', 'LoginFailed'] as const
export const AUDIT_ENTITY_TYPES = ['User', 'Club', 'ClubMember'] as const
export const AUDIT_SORT_FIELDS: Record<string, string> = { at: 'at' }

export function useAuditLogs(params: AuditLogsParams, options: { enabled?: boolean } = {}) {
  return usePagedQuery<AuditLogDto>(auditLogsKeys.list(params), '/api/audit-logs', params, options)
}
