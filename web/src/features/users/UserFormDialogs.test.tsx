import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { makeUser, usersPage } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { OWN_ACCOUNT_HINT } from './UserFormDialogs'

describe('User dialogs', () => {
  it('validates the create form before sending anything', async () => {
    let posted = false
    server.use(
      http.get(`${API}/api/users`, () => HttpResponse.json(usersPage())),
      http.post(`${API}/api/users`, () => {
        posted = true
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const { user } = renderApp('/users', { role: Roles.Admin })

    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const dialog = await screen.findByRole('dialog', { name: 'New user' })
    await user.type(within(dialog).getByLabelText('Email'), 'not-an-email')
    await user.type(within(dialog).getByLabelText('Password'), 'short')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Full name is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a valid email address')).toBeInTheDocument()
    expect(within(dialog).getByText('At least 8 characters')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a role')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('shows a 409 duplicate under the email field', async () => {
    let body: unknown
    server.use(
      http.get(`${API}/api/users`, () => HttpResponse.json(usersPage())),
      http.post(`${API}/api/users`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ status: 409, title: 'Email is already registered' }, { status: 409 })
      }),
    )
    const { user } = renderApp('/users', { role: Roles.Admin })

    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const dialog = await screen.findByRole('dialog', { name: 'New user' })
    await user.type(within(dialog).getByLabelText('Full name'), 'New Person')
    await user.type(within(dialog).getByLabelText('Email'), 'kavindi@campusspace.local')
    await user.type(within(dialog).getByLabelText('Password'), 'Password#1')
    await user.click(within(dialog).getByRole('combobox', { name: 'Role' }))
    await user.click(await screen.findByRole('option', { name: 'Lecturer' }))
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    const email = within(dialog).getByLabelText('Email')
    expect(await within(dialog).findByText('Email is already registered')).toBeInTheDocument()
    expect(email).toHaveAttribute('aria-invalid', 'true')
    expect(body).toEqual({
      fullName: 'New Person',
      email: 'kavindi@campusspace.local',
      password: 'Password#1',
      role: 'Lecturer',
    })
  })

  it("disables role and active on the admin's own row", async () => {
    // makeAuth signs in as user id 1.
    const self = makeUser(Roles.Admin, { id: 1, fullName: 'Me Admin' })
    const other = makeUser(Roles.Student, { id: 2, fullName: 'Other Student', isActive: false })
    server.use(http.get(`${API}/api/users`, () => HttpResponse.json(usersPage([self, other]))))
    const { user } = renderApp('/users', { role: Roles.Admin })

    expect(await screen.findByText('Inactive')).toBeInTheDocument()

    await user.click(await screen.findByRole('button', { name: 'Edit Me Admin' }))
    let dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('combobox', { name: 'Role' })).toHaveAttribute('aria-disabled', 'true')
    expect(within(dialog).getByRole('switch', { name: 'Active' })).toBeDisabled()
    expect(within(dialog).getByText(OWN_ACCOUNT_HINT)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await user.click(screen.getByRole('button', { name: 'Edit Other Student' }))
    dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('combobox', { name: 'Role' })).not.toHaveAttribute('aria-disabled')
    expect(within(dialog).getByRole('switch', { name: 'Active' })).toBeEnabled()
  })
})
