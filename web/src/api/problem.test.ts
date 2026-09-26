import { AxiosError, AxiosHeaders } from 'axios'
import { parseProblem } from './problem'

const axiosError = (status: number, data: unknown) =>
  new AxiosError('fail', 'ERR', undefined, undefined, {
    status,
    data,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
  })

describe('parseProblem', () => {
  it('reads title, status, traceId and camelCases field names', () => {
    const problem = parseProblem(
      axiosError(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        traceId: 'abc123',
        errors: { Email: ['The Email field is not a valid e-mail address.'], '$.password': ['Bad'] },
      }),
    )

    expect(problem).toEqual({
      title: 'One or more validation errors occurred.',
      status: 400,
      traceId: 'abc123',
      fieldErrors: { email: ['The Email field is not a valid e-mail address.'], password: ['Bad'] },
    })
  })

  it('handles errors that are not HTTP responses', () => {
    expect(parseProblem(new Error('boom')).title).toBe('Something went wrong')
  })
})
