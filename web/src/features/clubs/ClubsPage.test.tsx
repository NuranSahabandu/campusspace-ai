import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { CLUBS, pageOf } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

describe('ClubsPage', () => {
  it('lists clubs and sends includeInactive only when toggled on', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API}/api/clubs`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(pageOf(CLUBS))
      }),
    )
    const { user } = renderApp('/clubs', { role: Roles.Admin })

    expect(await screen.findByRole('link', { name: 'Robotics Club' })).toHaveAttribute('href', '/clubs/1')
    expect(screen.getByText('Kavindi Perera')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(requests.at(-1)?.has('includeInactive')).toBe(false)

    await user.click(screen.getByRole('switch', { name: 'Include inactive' }))
    await waitFor(() => expect(requests.at(-1)?.get('includeInactive')).toBe('true'))
  })
})
