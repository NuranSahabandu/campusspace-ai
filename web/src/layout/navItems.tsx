import type { ReactElement } from 'react'
import AssignmentIcon from '@mui/icons-material/Assignment'
import DashboardIcon from '@mui/icons-material/Dashboard'
import FactCheckIcon from '@mui/icons-material/FactCheck'
import GroupsIcon from '@mui/icons-material/Groups'
import HistoryIcon from '@mui/icons-material/History'
import MeetingRoomIcon from '@mui/icons-material/MeetingRoom'
import PeopleIcon from '@mui/icons-material/People'
import PsychologyIcon from '@mui/icons-material/Psychology'
import VideocamIcon from '@mui/icons-material/Videocam'
import { Roles, STAFF_ROLES } from '../auth/roles'

export interface NavItem {
  path: string
  label: string
  icon: ReactElement
  roles: readonly string[]
}

/** One list drives the drawer; the routes in App.tsx enforce the same roles. */
export const NAV_ITEMS: readonly NavItem[] = [
  { path: '/', label: 'Dashboard', icon: <DashboardIcon />, roles: STAFF_ROLES },
  { path: '/rooms', label: 'Rooms', icon: <MeetingRoomIcon />, roles: STAFF_ROLES },
  { path: '/equipment', label: 'Equipment', icon: <VideocamIcon />, roles: STAFF_ROLES },
  { path: '/requests', label: 'Requests', icon: <AssignmentIcon />, roles: STAFF_ROLES },
  { path: '/approvals', label: 'Approvals', icon: <FactCheckIcon />, roles: STAFF_ROLES },
  { path: '/agent-runs', label: 'Agent runs', icon: <PsychologyIcon />, roles: STAFF_ROLES },
  { path: '/users', label: 'Users', icon: <PeopleIcon />, roles: [Roles.Admin] },
  { path: '/clubs', label: 'Clubs', icon: <GroupsIcon />, roles: [Roles.Admin] },
  { path: '/audit-logs', label: 'Audit log', icon: <HistoryIcon />, roles: [Roles.Admin] },
]

export const navItemsFor = (role: string | undefined) =>
  role ? NAV_ITEMS.filter((item) => item.roles.includes(role)) : []
