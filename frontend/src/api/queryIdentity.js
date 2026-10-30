import http from './http'

export const inspectQueryIdentity = lookupToken => http.post('/QueryIdentity/inspect', { lookupToken })
export const approveQueryIdentity = (lookupToken, password) => http.post('/QueryIdentity/approve', { lookupToken, password })
