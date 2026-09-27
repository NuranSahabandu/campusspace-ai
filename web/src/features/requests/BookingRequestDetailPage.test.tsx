import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { requestsHandlers } from './requestsHandlers'

const card = (name: string) => screen.getByRole('region', { name })

describe('BookingRequestDetailPage', () => {
  it('shows the header and every card of a club request', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Robotics Club Arduino workshop' })).toBeInTheDocument()
    expect(screen.getAllByText('Waiting for approval').length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: 'Back to requests' })).toHaveAttribute('href', '/requests')

    const requester = card('Requester')
    expect(within(requester).getByText('Kavindi Perera')).toBeInTheDocument()
    expect(within(requester).getByText('kavindi@campusspace.local')).toBeInTheDocument()
    expect(within(requester).getByText('Robotics Club')).toBeInTheDocument()

    const booking = card('Booking')
    expect(within(booking).getByText('Tue 20 Oct 2026, 14:00–17:00')).toBeInTheDocument()
    expect(within(booking).getByText('40')).toBeInTheDocument()
    expect(within(booking).getByText('LKR 12,500.50')).toBeInTheDocument()

    expect(within(card('Required features')).getByText('Projector')).toBeInTheDocument()
    const equipment = card('Equipment')
    expect(within(equipment).getByText('MIC-WIRELESS — Wireless microphone × 1')).toBeInTheDocument()
    expect(within(equipment).getByText('PROJ-PORTABLE — Portable projector × 2')).toBeInTheDocument()

    expect(
      within(card('Agent proposal')).getByText(
        'No agent proposal yet. The proposal, validation checklist and agent trace will appear here.',
      ),
    ).toBeInTheDocument()
  })

  it('shows the requester notes as plain text with the line break kept', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    const notes = within(await screen.findByRole('region', { name: 'Requester notes (as written by the requester)' }))
      .getByTestId('request-notes')
    expect(notes.textContent).toBe('Please keep <b>bold</b> as typed.\nWe need extension cords.')
    // Markup stays text: no element was created from it.
    expect(notes.querySelector('b')).toBeNull()
    expect(notes.children).toHaveLength(0)
    expect(notes).toHaveStyle({ whiteSpace: 'pre-wrap' })
  })

  it('lists the status history oldest first, with System for automatic changes', async () => {
    requestsHandlers()
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    const items = within(await screen.findByRole('list', { name: 'Status timeline' })).getAllByRole('listitem')
    expect(items.map((i) => within(i).getByText(/^(Submitted|Processing|Waiting for approval)$/).textContent)).toEqual([
      'Submitted',
      'Processing',
      'Waiting for approval',
    ])
    expect(items[0]).toHaveTextContent('Kavindi Perera')
    expect(items[0]).toHaveTextContent('27 Sept 2026, 14:30')
    expect(items[1]).toHaveTextContent('System')
    expect(items[2]).toHaveTextContent('System')
    expect(items[2]).toHaveTextContent('Proposal ready for review')
  })

  it('shows an academic booking with no notes', async () => {
    requestsHandlers()
    renderApp('/requests/2', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByRole('heading', { level: 1, name: 'Guest lecture: AI in agriculture' })).toBeInTheDocument()
    expect(within(card('Requester')).getByText('Academic booking')).toBeInTheDocument()
    expect(within(card('Booking')).getByText('LKR 0.00')).toBeInTheDocument()
    expect(within(card('Requester notes (as written by the requester)')).getByText('None')).toBeInTheDocument()
  })

  it.each(['/requests/999', '/requests/403', '/requests/abc'])('shows "Request not found" for %s', async (route) => {
    requestsHandlers()
    renderApp(route, { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Request not found/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to requests' })).toHaveAttribute('href', '/requests')
  })

  it('offers Retry on a server error', async () => {
    requestsHandlers()
    server.use(
      http.get(`${API}/api/booking-requests/:id`, () =>
        HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 }),
      ),
    )
    renderApp('/requests/3', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Could not load the request/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
