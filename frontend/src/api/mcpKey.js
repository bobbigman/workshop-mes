import http from './http'

export const getMcpKeyList = () => http.get('/McpKey')
export const createMcpKey = () => http.post('/McpKey')
export const revokeMcpKey = (id) => http.post(`/McpKey/${id}/revoke`)
