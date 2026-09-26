import { Roles } from './roles'
import { isReturnablePath, returnPathFor, rolesForPath } from './routeAccess'

describe('routeAccess', () => {
  it('finds the roles of a page, including routes with parameters', () => {
    expect(rolesForPath('/rooms/12')).toEqual([Roles.FacilitiesOfficer])
    expect(rolesForPath('/clubs/3')).toEqual([Roles.Admin])
  })

  it.each(['/login', '/forbidden', '/no-such-page', 'https://evil.example/', '//evil.example/rooms'])(
    'never treats %s as a return path',
    (from) => {
      expect(isReturnablePath(from)).toBe(false)
      expect(returnPathFor(from, Roles.Admin)).toBe('/')
    },
  )

  it('returns to an allowed page with its query string', () => {
    expect(returnPathFor('/rooms?page=2', Roles.FacilitiesOfficer)).toBe('/rooms?page=2')
  })

  it("goes to the dashboard when the role cannot open the page, or there is none", () => {
    expect(returnPathFor('/audit-logs', Roles.FacilitiesOfficer)).toBe('/')
    expect(returnPathFor('/rooms', Roles.Admin)).toBe('/')
    expect(returnPathFor(undefined, Roles.Admin)).toBe('/')
  })
})
