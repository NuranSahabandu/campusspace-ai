import type { ReactElement } from 'react'
import ApartmentIcon from '@mui/icons-material/Apartment'
import AssignmentIcon from '@mui/icons-material/Assignment'
import AssignmentReturnIcon from '@mui/icons-material/AssignmentReturn'
import DashboardIcon from '@mui/icons-material/Dashboard'
import FactCheckIcon from '@mui/icons-material/FactCheck'
import GroupsIcon from '@mui/icons-material/Groups'
import HistoryIcon from '@mui/icons-material/History'
import InsightsIcon from '@mui/icons-material/Insights'
import Inventory2Icon from '@mui/icons-material/Inventory2'
import MeetingRoomIcon from '@mui/icons-material/MeetingRoom'
import PeopleIcon from '@mui/icons-material/People'
import PolicyIcon from '@mui/icons-material/Policy'
import PriceChangeIcon from '@mui/icons-material/PriceChange'
import PsychologyIcon from '@mui/icons-material/Psychology'
import VideocamIcon from '@mui/icons-material/Videocam'
import { type AppPath, ROUTE_ROLES } from '../auth/routeAccess'

export interface NavItem {
  path: AppPath
  label: string
  icon: ReactElement
  roles: readonly string[]
}

/** One list drives the drawer; its roles come from ROUTE_ROLES, which the routes in App.tsx also enforce. */
export const NAV_ITEMS: readonly NavItem[] = [
  { path: '/', label: 'Dashboard', icon: <DashboardIcon />, roles: ROUTE_ROLES['/'] },
  // The officer's main work queue, so first in their section.
  { path: '/requests', label: 'Booking requests', icon: <AssignmentIcon />, roles: ROUTE_ROLES['/requests'] },
  { path: '/rooms', label: 'Rooms', icon: <MeetingRoomIcon />, roles: ROUTE_ROLES['/rooms'] },
  { path: '/facilities/reference', label: 'Buildings & features', icon: <ApartmentIcon />, roles: ROUTE_ROLES['/facilities/reference'] },
  { path: '/equipment/types', label: 'Equipment types', icon: <VideocamIcon />, roles: ROUTE_ROLES['/equipment/types'] },
  { path: '/equipment/items', label: 'Equipment items', icon: <Inventory2Icon />, roles: ROUTE_ROLES['/equipment/items'] },
  { path: '/loans', label: 'Loans', icon: <AssignmentReturnIcon />, roles: ROUTE_ROLES['/loans'] },
  { path: '/pricing', label: 'Pricing', icon: <PriceChangeIcon />, roles: ROUTE_ROLES['/pricing'] },
  { path: '/policy', label: 'Booking policy', icon: <PolicyIcon />, roles: ROUTE_ROLES['/policy'] },
  { path: '/approvals', label: 'Approvals', icon: <FactCheckIcon />, roles: ROUTE_ROLES['/approvals'] },
  { path: '/agent-runs', label: 'Agent runs', icon: <PsychologyIcon />, roles: ROUTE_ROLES['/agent-runs'] },
  { path: '/reports', label: 'Reports', icon: <InsightsIcon />, roles: ROUTE_ROLES['/reports'] },
  { path: '/users', label: 'Users', icon: <PeopleIcon />, roles: ROUTE_ROLES['/users'] },
  { path: '/clubs', label: 'Clubs', icon: <GroupsIcon />, roles: ROUTE_ROLES['/clubs'] },
  { path: '/audit-logs', label: 'Audit log', icon: <HistoryIcon />, roles: ROUTE_ROLES['/audit-logs'] },
]

export const navItemsFor = (role: string | undefined) =>
  role ? NAV_ITEMS.filter((item) => item.roles.includes(role)) : []
