import http from './http'

export const inspectReportIdentity = (lookupToken) => http.post('/ReportIdentity/inspect', { lookupToken })
export const approveReportIdentity = (lookupToken, password) => http.post('/ReportIdentity/approve', { lookupToken, password })
