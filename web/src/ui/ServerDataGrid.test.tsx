import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { GridColDef } from '@mui/x-data-grid'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { usePagedQuery } from '../api/list'
import { useServerTable } from '../hooks/useServerTable'
import { AppProviders } from '../providers'
import { createQueryClient } from '../queryClient'
import { API, server } from '../test/server'
import { ServerDataGrid } from './ServerDataGrid'

interface Thing {
  id: number
  name: string
}

const columns: GridColDef<Thing>[] = [{ field: 'name', headerName: 'Name', flex: 1 }]
const SORT_FIELDS = { name: 'name' }

function ThingsGrid({ urlState }: { urlState: boolean }) {
  const table = useServerTable({ sortFields: SORT_FIELDS, pageSize: 10, urlState })
  const query = usePagedQuery<Thing>(['things', table.params], '/api/things', table.params)
  return <ServerDataGrid query={query} columns={columns} gridProps={table.gridProps} noun="things" emptyText="None" />
}

describe('ServerDataGrid + useServerTable', () => {
  // urlState keeps the same behaviour, with the state in the URL instead of the component.
  it.each([false, true])('sends page and sort changes as API query params (urlState %s)', async (urlState) => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API}/api/things`, ({ request }) => {
        const params = new URL(request.url).searchParams
        requests.push(params)
        const page = Number(params.get('page'))
        const items = Array.from({ length: 10 }, (_, i) => ({ id: (page - 1) * 10 + i + 1, name: `Thing ${(page - 1) * 10 + i + 1}` }))
        return HttpResponse.json({ items, page, pageSize: 10, total: 25 })
      }),
    )
    const user = userEvent.setup()
    render(
      <AppProviders client={createQueryClient({ retry: false })}>
        <MemoryRouter>
          <ThingsGrid urlState={urlState} />
        </MemoryRouter>
      </AppProviders>,
    )

    expect(await screen.findByText('Thing 1')).toBeInTheDocument()
    expect(requests.at(-1)?.get('page')).toBe('1')
    expect(requests.at(-1)?.get('pageSize')).toBe('10')
    expect(requests.at(-1)?.has('sort')).toBe(false)

    await user.click(screen.getByRole('button', { name: /next page/i }))
    expect(await screen.findByText('Thing 11')).toBeInTheDocument()
    expect(requests.at(-1)?.get('page')).toBe('2')

    // Sorting starts again from page 1.
    await user.click(screen.getByRole('columnheader', { name: /name/i }))
    await waitFor(() => expect(requests.at(-1)?.get('sort')).toBe('name'))
    expect(requests.at(-1)?.get('page')).toBe('1')

    await user.click(screen.getByRole('columnheader', { name: /name/i }))
    await waitFor(() => expect(requests.at(-1)?.get('sort')).toBe('-name'))
  })
})
