import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { Roles } from '../../auth/roles'
import { makePricingRule, PRICING_NOW } from '../../test/fixtures'
import { API, server } from '../../test/server'
import { renderApp } from '../../test/utils'
import { useToastStore } from '../../ui/toastStore'
import { PAST_DATE_MESSAGE } from './PricingRuleFormDialog'
import { pricingHandlers } from './pricingHandlers'

// Date inputs take a whole value; typing them key by key is not supported in jsdom.
const setValue = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } })

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(PRICING_NOW)
})
afterEach(() => {
  vi.useRealTimers()
})

async function openNewRule() {
  pricingHandlers()
  const rendered = renderApp('/pricing', { role: Roles.FacilitiesOfficer })
  await rendered.user.click(await screen.findByRole('button', { name: 'New rule' }))
  const dialog = await screen.findByRole('dialog', { name: 'New pricing rule' })
  const { user } = rendered
  await user.click(within(dialog).getByRole('combobox', { name: 'Room type' }))
  await user.click(await screen.findByRole('option', { name: 'Seminar room' }))
  await user.click(within(dialog).getByRole('combobox', { name: 'Requester role' }))
  await user.click(await screen.findByRole('option', { name: 'Lecturer' }))
  return { ...rendered, dialog }
}

/** Records the POST body and replies with `reply`. */
function capturePost(reply: () => Response = () => HttpResponse.json(makePricingRule({ id: 20 }), { status: 201 })) {
  const bodies: unknown[] = []
  server.use(
    http.post(`${API}/api/pricing-rules`, async ({ request }) => {
      bodies.push(await request.json())
      return reply()
    }),
  )
  return bodies
}

describe('PricingRuleFormDialog', () => {
  it('limits Valid from to campus today or later', async () => {
    const { dialog } = await openNewRule()

    expect(within(dialog).getByLabelText('Valid from')).toHaveAttribute('min', '2026-09-27')
  })

  it('disables the rate when exempt and sends 0', async () => {
    const bodies = capturePost()
    const { user, dialog } = await openNewRule()
    const rate = within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' })
    await user.type(rate, '250')

    await user.click(within(dialog).getByRole('switch', { name: 'Exempt (free)' }))
    expect(rate).toBeDisabled()
    expect(rate).toHaveValue('0')
    setValue(within(dialog).getByLabelText('Valid from'), '2026-10-15')
    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(bodies).toEqual([
        { roomType: 'SeminarRoom', requesterRole: 'Lecturer', hourlyRate: 0, isExempt: true, validFrom: '2026-10-15' },
      ]),
    )
    await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ message: 'Rule created' }))
  })

  it('rejects a start date in the past without calling the API', async () => {
    const bodies = capturePost()
    const { user, dialog } = await openNewRule()
    await user.type(within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' }), '600')
    setValue(within(dialog).getByLabelText('Valid from'), '2026-09-26')

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText(PAST_DATE_MESSAGE)).toBeInTheDocument()
    expect(bodies).toEqual([])
  })

  it('rejects a rate with more than 2 decimal places', async () => {
    const bodies = capturePost()
    const { user, dialog } = await openNewRule()
    await user.type(within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' }), '12.345')
    setValue(within(dialog).getByLabelText('Valid from'), '2026-10-15')

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('Rate can have at most 2 decimal places.')).toBeInTheDocument()
    expect(bodies).toEqual([])
  })

  it('shows a duplicate rule (409) on Valid from', async () => {
    capturePost(() =>
      HttpResponse.json({ status: 409, title: 'A rule for this room type, role and date already exists' }, { status: 409 }),
    )
    const { user, dialog } = await openNewRule()
    await user.type(within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' }), '600')
    setValue(within(dialog).getByLabelText('Valid from'), '2026-10-15')

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('A rule for this room type, role and date already exists')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Valid from')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a server 400 on its field', async () => {
    capturePost(() =>
      HttpResponse.json(
        { status: 400, title: 'An exempt rule must have a rate of 0.', errors: { HourlyRate: ['An exempt rule must have a rate of 0.'] } },
        { status: 400 },
      ),
    )
    const { user, dialog } = await openNewRule()
    await user.type(within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' }), '600')
    setValue(within(dialog).getByLabelText('Valid from'), '2026-10-15')

    await user.click(within(dialog).getByRole('button', { name: 'Create' }))

    expect(await within(dialog).findByText('An exempt rule must have a rate of 0.')).toBeInTheDocument()
    expect(within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' })).toHaveAttribute('aria-invalid', 'true')
  })

  it('edits a scheduled rule', async () => {
    pricingHandlers()
    let body: unknown
    server.use(
      http.put(`${API}/api/pricing-rules/9`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makePricingRule({ id: 9 }))
      }),
    )
    const { user } = renderApp('/pricing', { role: Roles.FacilitiesOfficer })

    await user.click(await screen.findByRole('button', { name: 'Edit SeminarRoom Student from 1 Nov 2026' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit Seminar room / Student from 1 Nov 2026' })
    const rate = within(dialog).getByRole('textbox', { name: 'Hourly rate (LKR)' })
    expect(rate).toHaveValue('750')
    await user.clear(rate)
    await user.type(rate, '800.50')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(body).toEqual({
        roomType: 'SeminarRoom',
        requesterRole: 'Student',
        hourlyRate: 800.5,
        isExempt: false,
        validFrom: '2026-11-01',
      }),
    )
    await waitFor(() => expect(useToastStore.getState().current).toMatchObject({ message: 'Rule updated' }))
  })
})
