import http from './http'

export const getMcpKeyFactories = () => http.get('/McpKey/factories')
export const getMcpKeyList = (params) => http.get('/McpKey', { params })
export const createMcpKey = (data) => http.post('/McpKey', data)
export const revokeMcpKey = (id) => http.post(`/McpKey/${id}/revoke`)
