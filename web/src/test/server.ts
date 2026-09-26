import { setupServer } from 'msw/node'

export const API = 'http://localhost:5080'

/** Tests add their own handlers with server.use(...). */
export const server = setupServer()
