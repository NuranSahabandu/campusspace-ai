import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { POLICY_SETTINGS } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { APPLIES_IMMEDIATELY } from './PolicyPage'
import { policyHandlers } from './policyHandlers'

// Time inputs take a whole value; typing them key by key is not supported in jsdom.
const setValue = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } })

async function openPolicy(put?: Parameters<typeof policyHandlers>[0]) {
  const { bodies } = policyHandlers(put)
  const rendered = renderApp('/policy', { role: Roles.FacilitiesOfficer })
  await screen.findByRole('textbox', { name: 'Capacity ratio' })
  return { ...rendered, bodies }
}

const field = (name: string) => screen.getByRole('textbox', { name })
const saveButton = () => screen.getByRole('button', { name: 'Save' })

describe('PolicyPage', () => {
  it('renders the stored values, including a closed Sunday', async () => {
    await openPolicy()

    expect(screen.getByRole('heading', { name: 'Booking policy' })).toBeInTheDocument()
    expect(screen.getByText(/^Last updated .*2026.* by Mr\. Perera$/)).toBeInTheDocument()
    expect(screen.getByLabelText('Monday opens')).toHaveValue('08:00')
    expect(screen.getByLabelText('Saturday closes')).toHaveValue('16:00')
    expect(screen.getByRole('switch', { name: 'Sunday open' })).not.toBeChecked()
    expect(screen.getByLabelText('Sunday opens')).toBeDisabled()
    expect(field('Capacity ratio')).toHaveValue('3')
    expect(field('Maximum duration')).toHaveValue('8')
    expect(screen.getByRole('combobox', { name: 'Slot granularity' })).toHaveTextContent('30 minutes')
    expect(screen.getByText('A room may seat at most this many times the attendees')).toBeInTheDocument()
    // The time pickers step by the granularity (30 minutes = 1800 s).
    expect(screen.getByLabelText('Monday opens')).toHaveAttribute('step', '1800')
  })

  it('confirms a ratio change and sends only that key', async () => {
    const { user, bodies } = await openPolicy()
    expect(saveButton()).toBeDisabled()

    await user.clear(field('Capacity ratio'))
    await user.type(field('Capacity ratio'), '2')
    await user.click(saveButton())

    const dialog = await screen.findByRole('dialog', { name: 'Save booking policy?' })
    expect(within(dialog).getByText(APPLIES_IMMEDIATELY)).toBeInTheDocument()
    expect(within(within(dialog).getByRole('list', { name: 'Changes' })).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'Capacity ratio: 3 → 2',
    ])
    expect(bodies).toEqual([])
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(bodies).toEqual([{ settings: [{ key: 'max_capacity_ratio', value: '2' }] }]))
    await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ message: 'Booking policy saved' }))
    // The form now starts from the saved values.
    await waitFor(() => expect(saveButton()).toBeDisabled())
    expect(field('Capacity ratio')).toHaveValue('2')
  })

  it('sends opening hours as compact JSON in day order', async () => {
    const { user, bodies } = await openPolicy()

    await user.click(screen.getByRole('switch', { name: 'Saturday open' }))
    await user.click(saveButton())
    const dialog = await screen.findByRole('dialog', { name: 'Save booking policy?' })
    expect(within(dialog).getByText('Opening hours, Sat: 08:00–16:00 → closed')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({
      settings: [
        {
          key: 'opening_hours',
          value:
            '{"mon":{"open":"08:00","close":"20:00"},"tue":{"open":"08:00","close":"20:00"},"wed":{"open":"08:00","close":"20:00"},' +
            '"thu":{"open":"08:00","close":"20:00"},"fri":{"open":"08:00","close":"20:00"},"sat":null,"sun":null}',
        },
      ],
    })
  })

  it('rejects a time off the new granularity and sends nothing', async () => {
    const { user, bodies } = await openPolicy()

    await user.click(screen.getByRole('combobox', { name: 'Slot granularity' }))
    await user.click(await screen.findByRole('option', { name: '60 minutes' }))
    expect(screen.getByLabelText('Saturday opens')).toHaveAttribute('step', '3600')
    setValue(screen.getByLabelText('Saturday opens'), '08:30')
    await user.click(saveButton())

    expect(await screen.findByText('Saturday: times must be on a 60-minute boundary.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bodies).toEqual([])
  })

  it('rejects a duration longer than the longest open day', async () => {
    const { user, bodies } = await openPolicy()

    await user.clear(field('Maximum duration'))
    await user.type(field('Maximum duration'), '13')
    await user.click(saveButton())

    expect(await screen.findByText("Can't be longer than the longest open day (12 h).")).toBeInTheDocument()
    expect(field('Maximum duration')).toHaveAttribute('aria-invalid', 'true')
    expect(bodies).toEqual([])
  })

  it('needs at least one open day', async () => {
    const { user } = await openPolicy()

    for (const day of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'])
      await user.click(screen.getByRole('switch', { name: `${day} open` }))
    await user.click(saveButton())

    expect(await screen.findByText('At least one day must be open.')).toBeInTheDocument()
  })

  it('shows a server error on the setting it is keyed by', async () => {
    const message = "Can't be longer than the longest open day (10 h)."
    const { user } = await openPolicy(() =>
      HttpResponse.json({ status: 400, title: message, errors: { max_duration_hours: [message] } }, { status: 400 }),
    )

    await user.clear(field('Maximum duration'))
    await user.type(field('Maximum duration'), '10')
    await user.click(saveButton())
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(message)).toBeInTheDocument()
    // The confirm dialog closes after the error; until then the page behind it is hidden from queries.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(field('Maximum duration')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a server opening_hours error above the hours editor', async () => {
    const message = 'sat: times must be on a 60-minute boundary.'
    const { user } = await openPolicy(() =>
      HttpResponse.json({ status: 400, title: message, errors: { opening_hours: [message] } }, { status: 400 }),
    )

    setValue(screen.getByLabelText('Saturday closes'), '17:00')
    await user.click(saveButton())
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(message)
  })

  it('Reset restores the loaded values', async () => {
    const { user } = await openPolicy()

    await user.clear(field('Capacity ratio'))
    await user.type(field('Capacity ratio'), '5')
    await user.click(screen.getByRole('switch', { name: 'Sunday open' }))
    expect(saveButton()).toBeEnabled()
    await user.click(screen.getByRole('button', { name: 'Reset' }))

    expect(field('Capacity ratio')).toHaveValue('3')
    expect(screen.getByRole('switch', { name: 'Sunday open' })).not.toBeChecked()
    expect(saveButton()).toBeDisabled()
  })

  it('shows an error with Retry when loading fails', async () => {
    let calls = 0
    server.use(
      http.get(`${API}/api/policy-settings`, () => {
        calls += 1
        return HttpResponse.json({ status: 500, title: 'An unexpected error occurred' }, { status: 500 })
      }),
    )
    const { user } = renderApp('/policy', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText(/Could not load the booking policy/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    await waitFor(() => expect(calls).toBe(2))
  })

  it('says "Default values" when no officer has changed anything', async () => {
    server.use(
      http.get(`${API}/api/policy-settings`, () =>
        HttpResponse.json(POLICY_SETTINGS.map((s) => ({ ...s, updatedByName: null }))),
      ),
    )
    renderApp('/policy', { role: Roles.FacilitiesOfficer })

    expect(await screen.findByText('Default values')).toBeInTheDocument()
  })
})
