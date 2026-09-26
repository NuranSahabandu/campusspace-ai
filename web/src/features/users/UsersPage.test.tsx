import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { usersPage } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { toSortParam } from './usersSort'

describe('UsersPage', () => {
  it('renders rows from the API', async () => {
    let query: URLSearchParams | undefined
    server.use(
      http.get(`${API}/api/users`, ({ request }) => {
        query = new URL(request.url).searchParams
        return HttpResponse.json(usersPage())
      }),
    )
    renderApp('/users', { role: Roles.Admin })

    expect(await screen.findByText('Kavindi Perera')).toBeInTheDocument()
    expect(screen.getByText('admin@campusspace.local')).toBeInTheDocument()
    expect(query?.get('page')).toBe('1')
    expect(query?.get('pageSize')).toBe('20')
  })

  it('shows the empty state when nothing matches', async () => {
    server.use(http.get(`${API}/api/users`, () => HttpResponse.json(usersPage([]))))
    renderApp('/users', { role: Roles.Admin })

    expect(await screen.findByText('No users match')).toBeInTheDocument()
  })

  it('shows an error with Retry on 500, and Retry refetches', async () => {
    let calls = 0
    server.use(
      http.get(`${API}/api/users`, () => {
        calls++
        return calls === 1
          ? HttpResponse.json({ status: 500, title: 'An unexpected error occurred.' }, { status: 500 })
          : HttpResponse.json(usersPage())
      }),
    )
    const { user } = renderApp('/users', { role: Roles.Admin })

    expect(await screen.findByText(/could not load users/i)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Kavindi Perera')).toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('maps grid sorting to the API sort parameter', () => {
    expect(toSortParam([])).toBeUndefined()
    expect(toSortParam([{ field: 'fullName', sort: 'asc' }])).toBe('name')
    expect(toSortParam([{ field: 'createdAt', sort: 'desc' }])).toBe('-createdAt')
  })
})
