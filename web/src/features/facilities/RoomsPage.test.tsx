import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { BUILDINGS, FEATURES, ROOMS } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

/** Records every GET /api/rooms query. total > pageSize so the grid can move to page 2. */
function roomsHandlers(total = ROOMS.length) {
  const requests: URLSearchParams[] = []
  server.use(
    http.get(`${API}/api/buildings`, () => HttpResponse.json(BUILDINGS)),
    http.get(`${API}/api/features`, () => HttpResponse.json(FEATURES)),
    http.get(`${API}/api/rooms`, ({ request }) => {
      const params = new URL(request.url).searchParams
      requests.push(params)
      return HttpResponse.json({ items: ROOMS, page: Number(params.get('page')), pageSize: 20, total })
    }),
  )
  return requests
}

describe('RoomsPage', () => {
  it('lists rooms with building, feature chips and status', async () => {
    const requests = roomsHandlers()
    renderApp('/rooms', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('link', { name: 'A301' })).toHaveAttribute('href', '/rooms/1')
    const row = screen.getByRole('link', { name: 'N201' }).closest('[role="row"]') as HTMLElement
    expect(within(row).getByText('NB')).toBeInTheDocument()
    expect(within(row).getByText('Computer lab')).toBeInTheDocument()
    expect(within(row).getByText('projector')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(requests[0].get('sort')).toBe('code')
    expect(requests[0].has('includeInactive')).toBe(false)
  })

  it('sends chosen features as one comma-separated parameter', async () => {
    const requests = roomsHandlers()
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('link', { name: 'A301' })

    const features = screen.getByRole('combobox', { name: 'Features (all of)' })
    await user.click(features)
    await user.click(await screen.findByRole('option', { name: 'computers' }))
    await user.click(features)
    await user.click(await screen.findByRole('option', { name: 'projector' }))

    await waitFor(() => expect(requests.at(-1)?.get('features')).toBe('computers,projector'))
  })

  it('sends minCapacity and buildingId and goes back to page 1', async () => {
    const requests = roomsHandlers(45)
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })
    await screen.findByRole('link', { name: 'A301' })

    await user.click(screen.getByRole('button', { name: 'Go to next page' }))
    await waitFor(() => expect(requests.at(-1)?.get('page')).toBe('2'))

    await user.type(screen.getByLabelText('Min capacity'), '45')
    await waitFor(() => expect(requests.at(-1)?.get('minCapacity')).toBe('45'))
    expect(requests.at(-1)?.get('page')).toBe('1')

    await user.click(screen.getByRole('button', { name: 'Go to next page' }))
    await waitFor(() => expect(requests.at(-1)?.get('page')).toBe('2'))

    await user.click(screen.getByRole('combobox', { name: 'Building' }))
    await user.click(await screen.findByRole('option', { name: 'NB · New Building' }))
    await waitFor(() => expect(requests.at(-1)?.get('buildingId')).toBe('2'))
    expect(requests.at(-1)?.get('page')).toBe('1')
    expect(requests.at(-1)?.get('minCapacity')).toBe('45')
  })

  it('asks for confirmation before deactivating a room', async () => {
    roomsHandlers()
    let deleted: string | null = null
    server.use(
      http.delete(`${API}/api/rooms/:id`, ({ params }) => {
        deleted = params.id as string
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/rooms', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Deactivate A301' }))
    const dialog = await screen.findByRole('dialog', { name: 'Deactivate room?' })
    expect(deleted).toBeNull()
    // An inactive room has no Deactivate action; it is reactivated from Edit.
    expect(screen.queryByRole('button', { name: 'Deactivate A102' })).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(deleted).toBe('1'))
  })
})
