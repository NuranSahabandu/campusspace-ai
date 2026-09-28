import {
  CANCELLABLE_STATUSES,
  DEFAULT_STATUS_GROUP,
  REQUEST_STATUSES,
  STATUS_GROUPS,
  isCancellable,
  isStatusGroup,
  requestStatusChipStyle,
  requestStatusLabel,
} from './requestStatus'

describe('requestStatus', () => {
  it('lists the nine backend statuses', () => {
    expect(REQUEST_STATUSES).toEqual([
      'Submitted',
      'AgentProcessing',
      'PendingApproval',
      'Approved',
      'Completed',
      'AgentFailed',
      'RevisionRequested',
      'Rejected',
      'Cancelled',
    ])
  })

  it.each([
    ['Submitted', 'Submitted'],
    ['AgentProcessing', 'Processing'],
    ['PendingApproval', 'Waiting for approval'],
    ['Approved', 'Approved'],
    ['Completed', 'Completed'],
    ['RevisionRequested', 'Needs revision'],
    ['Rejected', 'Rejected'],
    ['Cancelled', 'Cancelled'],
    ['AgentFailed', 'Failed'],
  ])('labels %s as "%s" (the mobile label)', (status, label) => {
    expect(requestStatusLabel(status)).toBe(label)
  })

  it('shows an unknown status as sent, with a neutral chip', () => {
    expect(requestStatusLabel('Archived')).toBe('Archived')
    expect(requestStatusChipStyle('Archived')).toEqual({ color: 'default', variant: 'outlined' })
  })

  it('gives every status a distinct chip style', () => {
    const styles = REQUEST_STATUSES.map((s) => JSON.stringify(requestStatusChipStyle(s)))
    expect(new Set(styles).size).toBe(REQUEST_STATUSES.length)
  })

  it('groups statuses like the mobile filter chips, covering each status once', () => {
    expect(STATUS_GROUPS.open.statuses).toEqual(['Submitted', 'AgentProcessing', 'PendingApproval', 'RevisionRequested'])
    expect(STATUS_GROUPS.approved.statuses).toEqual(['Approved', 'Completed'])
    expect(STATUS_GROUPS.closed.statuses).toEqual(['Rejected', 'Cancelled', 'AgentFailed'])
    expect(STATUS_GROUPS.all.statuses).toEqual([])
    const grouped = [...STATUS_GROUPS.open.statuses, ...STATUS_GROUPS.approved.statuses, ...STATUS_GROUPS.closed.statuses]
    expect([...grouped].sort()).toEqual([...REQUEST_STATUSES].sort())
  })

  it('defaults to Open and recognises group keys only', () => {
    expect(DEFAULT_STATUS_GROUP).toBe('open')
    expect(isStatusGroup('closed')).toBe(true)
    expect(isStatusGroup('toString')).toBe(false)
  })

  it('cancels from the same statuses as the backend state machine and mobile', () => {
    expect(CANCELLABLE_STATUSES).toEqual(['Submitted', 'PendingApproval', 'Approved'])
    expect(REQUEST_STATUSES.filter(isCancellable)).toEqual(['Submitted', 'PendingApproval', 'Approved'])
    expect(isCancellable('Unknown')).toBe(false)
  })
})
