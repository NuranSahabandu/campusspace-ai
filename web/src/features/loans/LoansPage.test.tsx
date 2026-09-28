import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { loansHandlers } from './loansHandlers'

const rowOf = (text: string) => screen.getByText(text).closest('[role="row"]') as HTMLElement
const filterButton = (name: string) => within(screen.getByRole('group', { name: 'Loan filter' })).getByRole('button', { name })

// jsdom has no object URLs: stub them so the test can see what was created and revoked.
const createObjectURL = vi.fn((blob: Blob) => (blob instanceof Blob ? 'blob:photo-2' : 'blob:bad'))
const revokeObjectURL = vi.fn()
beforeEach(() => {
  createObjectURL.mockClear()
  revokeObjectURL.mockClear()
  Object.assign(URL, { createObjectURL, revokeObjectURL })
})
afterEach(() => {
  Reflect.deleteProperty(URL, 'createObjectURL')
  Reflect.deleteProperty(URL, 'revokeObjectURL')
})

async function openLoan(user: ReturnType<typeof renderApp>['user'], assetTag: string) {
  await user.click(await screen.findByText(assetTag))
  return screen.findByRole('region', { name: 'Loan details' })
}

describe('LoansPage', () => {
  it('lists loans with item, room, checkout, due, return and flags, newest due first', async () => {
    const { listRequests } = loansHandlers()
    renderApp('/loans', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Loans' })).toBeInTheDocument()
    await screen.findByText('MIC-0001')
    expect(listRequests[0].get('sort')).toBe('-dueAt')
    expect(listRequests[0].has('overdue')).toBe(false)
    expect(filterButton('All')).toHaveAttribute('aria-pressed', 'true')

    const overdue = rowOf('MIC-0001')
    expect(within(overdue).getByText('MIC-WIRELESS')).toBeInTheDocument()
    expect(within(overdue).getByText('A301')).toBeInTheDocument()
    expect(within(overdue).getByText('Sunil Silva')).toBeInTheDocument()
    expect(within(overdue).getByText('Out')).toBeInTheDocument()
    expect(within(overdue).getByText('Overdue')).toBeInTheDocument()

    const damaged = rowOf('PROJ-0002')
    expect(within(damaged).getByText('Damaged', { selector: '.MuiChip-label' })).toBeInTheDocument()
    expect(within(damaged).getByText('Late return')).toBeInTheDocument()
    expect(within(damaged).queryByText('Overdue')).not.toBeInTheDocument()

    const good = rowOf('MIC-0003')
    expect(within(good).getByText('Good')).toBeInTheDocument()
    expect(within(good).queryByText('Late return')).not.toBeInTheDocument()
  })

  it('sends the Overdue filter as ?overdue= and keeps it in the URL', async () => {
    const { listRequests } = loansHandlers()
    const { user } = renderApp('/loans', { role: Roles.FacilitiesOfficer })
    await screen.findByText('MIC-0001')

    await user.click(filterButton('Overdue'))
    await waitFor(() => expect(listRequests.at(-1)?.get('overdue')).toBe('true'))
    expect(filterButton('Overdue')).toHaveAttribute('aria-pressed', 'true')

    await user.click(filterButton('Returned or not due'))
    await waitFor(() => expect(listRequests.at(-1)?.get('overdue')).toBe('false'))

    // Back to All: the first page is still cached (staleTime), so no new request is needed.
    await user.click(filterButton('All'))
    await waitFor(() => expect(filterButton('All')).toHaveAttribute('aria-pressed', 'true'))
    expect(await screen.findByText('MIC-0001')).toBeInTheDocument()
  })

  it.each([
    ['/loans?overdue=true', 'true', 'Overdue'],
    ['/loans?overdue=false', 'false', 'Returned or not due'],
    ['/loans?overdue=maybe', null, 'All'],
  ])('reads %s from the URL', async (route, sent, pressed) => {
    const { listRequests } = loansHandlers()
    renderApp(route, { role: Roles.FacilitiesOfficer })

    await screen.findByText('MIC-0001')
    expect(listRequests[0].get('overdue')).toBe(sent)
    expect(filterButton(pressed)).toHaveAttribute('aria-pressed', 'true')
  })

  it('sends the search', async () => {
    const { listRequests } = loansHandlers()
    const { user } = renderApp('/loans', { role: Roles.FacilitiesOfficer })
    await screen.findByText('MIC-0001')

    await user.type(screen.getByRole('textbox', { name: 'Asset tag or type code' }), 'PROJ')
    await waitFor(() => expect(listRequests.at(-1)?.get('search')).toBe('PROJ'))
  })

  it('opens an overdue loan with its details and no photo', async () => {
    loansHandlers()
    const { user } = renderApp('/loans', { role: Roles.FacilitiesOfficer })

    const drawer = await openLoan(user, 'MIC-0001')
    expect(await within(drawer).findByRole('heading', { name: 'Loan of MIC-0001' })).toBeInTheDocument()
    expect(within(drawer).getByText('MIC-0001 · MIC-WIRELESS')).toBeInTheDocument()
    expect(within(drawer).getByText('#21 · A301')).toBeInTheDocument()
    expect(within(drawer).getByText('Not yet')).toBeInTheDocument()
    expect(within(drawer).getByText('Overdue')).toBeInTheDocument()
    expect(within(drawer).queryByText('Damage photo')).not.toBeInTheDocument()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('shows the damage note as plain text and loads the photo through the authenticated client', async () => {
    const { photoAuth } = loansHandlers()
    const { user } = renderApp('/loans', { role: Roles.FacilitiesOfficer })

    const drawer = await openLoan(user, 'PROJ-0002')
    expect(await within(drawer).findByText('Damaged', { selector: 'p' })).toBeInTheDocument()
    // Markup in the note is text, and the line break is kept.
    expect(within(drawer).getByTestId('damage-note').textContent).toBe('Lens <b>cracked</b>\nCase dented')
    expect(drawer.querySelector('b')).toBeNull()

    const photo = await within(drawer).findByRole('img', { name: 'Damage photo for PROJ-0002' })
    expect(photoAuth).toEqual([`Bearer token-${Roles.FacilitiesOfficer}`])
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(createObjectURL.mock.calls[0][0]).toBeInstanceOf(Blob)
    expect(photo).toHaveAttribute('src', 'blob:photo-2')
    expect(revokeObjectURL).not.toHaveBeenCalled()

    // Closing the drawer unmounts the image, which revokes its object URL.
    await user.click(within(drawer).getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(revokeObjectURL).toHaveBeenCalledWith('blob:photo-2'))
  })

  it('revokes the photo URL when the page unmounts', async () => {
    loansHandlers()
    const { user, unmount } = renderApp('/loans', { role: Roles.FacilitiesOfficer })

    const drawer = await openLoan(user, 'PROJ-0002')
    await within(drawer).findByRole('img', { name: 'Damage photo for PROJ-0002' })
    unmount()

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:photo-2')
  })

  it('shows an error with Retry when the photo cannot be loaded', async () => {
    loansHandlers()
    server.use(
      http.get(`${API}/api/loans/:id/photo`, () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })),
    )
    const { user } = renderApp('/loans', { role: Roles.FacilitiesOfficer })

    const drawer = await openLoan(user, 'PROJ-0002')
    expect(await within(drawer).findByText(/Could not load the photo/)).toBeInTheDocument()
    expect(within(drawer).getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('shows the empty state', async () => {
    loansHandlers({ rows: [] })
    renderApp('/loans?overdue=true', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('No overdue loans')).toBeInTheDocument()
  })

  it('shows a 403 from the API as an error with Retry', async () => {
    server.use(
      http.get(`${API}/api/loans`, () =>
        HttpResponse.json({ title: 'Forbidden', status: 403, traceId: 'trace-403' }, { status: 403 }),
      ),
    )
    renderApp('/loans', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Could not load loans: Forbidden')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
