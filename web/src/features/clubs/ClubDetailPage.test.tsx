import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { clubDetail, makeUser, usersPage } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'

const rowOf = (name: string) => screen.getByText(name).closest('tr') as HTMLElement

describe('ClubDetailPage', () => {
  it('makes a member the representative and refreshes the club', async () => {
    let representativeId = 1
    let body: unknown
    server.use(
      http.get(`${API}/api/clubs/1`, () => HttpResponse.json(clubDetail(representativeId))),
      http.put(`${API}/api/clubs/1/representative`, async ({ request }) => {
        body = await request.json()
        representativeId = 2
        return HttpResponse.json(clubDetail(representativeId))
      }),
    )
    const { user } = renderApp('/clubs/1', { role: Roles.Admin })

    expect(await screen.findByRole('heading', { name: 'Robotics Club' })).toBeInTheDocument()
    expect(within(rowOf('Kavindi Perera')).getByText('Representative')).toBeInTheDocument()
    // Hidden for the current representative.
    expect(screen.queryByRole('button', { name: 'Make Kavindi Perera representative' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Make Ishan Silva representative' }))

    await waitFor(() => expect(within(rowOf('Ishan Silva')).getByText('Representative')).toBeInTheDocument())
    expect(within(rowOf('Kavindi Perera')).queryByText('Representative')).not.toBeInTheDocument()
    expect(body).toEqual({ userId: 2 })
  })

  it('asks for confirmation before removing a member', async () => {
    let deleted: string | null = null
    server.use(
      http.get(`${API}/api/clubs/1`, () => HttpResponse.json(clubDetail())),
      http.delete(`${API}/api/clubs/1/members/:userId`, ({ params }) => {
        deleted = params.userId as string
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { user } = renderApp('/clubs/1', { role: Roles.Admin })

    await user.click(await screen.findByRole('button', { name: 'Remove Dr. Fernando' }))
    const dialog = await screen.findByRole('dialog', { name: 'Remove member?' })
    expect(within(dialog).getByText('Remove Dr. Fernando from Robotics Club?')).toBeInTheDocument()
    expect(deleted).toBeNull()

    await user.click(within(dialog).getByRole('button', { name: 'Remove' }))

    await waitFor(() => expect(deleted).toBe('3'))
  })

  it('shows a 400 rule error inside the add-member dialog', async () => {
    const roles: (string | null)[] = []
    server.use(
      http.get(`${API}/api/clubs/1`, () => HttpResponse.json(clubDetail())),
      http.get(`${API}/api/users`, ({ request }) => {
        const role = new URL(request.url).searchParams.get('role')
        roles.push(role)
        return HttpResponse.json(
          usersPage(role === Roles.Student ? [makeUser(Roles.Student, { id: 7, fullName: 'Nethmi Rajapaksa' })] : []),
        )
      }),
      http.post(`${API}/api/clubs/1/members`, () =>
        HttpResponse.json(
          {
            status: 400,
            title: 'User does not exist or is inactive.',
            errors: { UserId: ['User does not exist or is inactive.'] },
          },
          { status: 400 },
        ),
      ),
    )
    const { user } = renderApp('/clubs/1', { role: Roles.Admin })

    await user.click(await screen.findByRole('button', { name: 'Add member' }))
    const dialog = await screen.findByRole('dialog', { name: /^Add member to/ })
    await user.click(within(dialog).getByRole('combobox', { name: 'Student or lecturer' }))
    await user.click(await screen.findByRole('option', { name: 'Nethmi Rajapaksa (Student)' }))
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(dialog).findByText('User does not exist or is inactive.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: /^Add member to/ })).toBeInTheDocument()
    // Only students and lecturers are offered.
    expect(new Set(roles)).toEqual(new Set([Roles.Student, Roles.Lecturer]))
  })

  it('shows "Club not found" for a non-numeric id without calling the API', async () => {
    const calls: string[] = []
    server.use(http.get(`${API}/api/clubs/:id`, ({ params }) => (calls.push(String(params.id)), HttpResponse.json({}))))
    renderApp('/clubs/abc', { role: Roles.Admin })

    expect(await screen.findByText('Club not found')).toBeInTheDocument()
    expect(calls).toEqual([])
  })
})
