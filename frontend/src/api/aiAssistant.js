import http from './http'

/** PC 内置 AI 助手（docs/26）。密钥不在前端。 */
export const getAiStatus = () => http.get('/ai-assistant/status')
export const testAiConnection = () => http.post('/ai-assistant/connection-test', null, { timeout: 60000 })
export const sendAiMessage = (data, signal) =>
  http.post('/ai-assistant/messages', data, { timeout: 120000, signal })
export const deleteAiConversation = (id) => http.delete(`/ai-assistant/conversations/${id}`)
export const searchAiWorkOrders = (data) =>
  http.post('/ai-assistant/work-orders/search', data, { timeout: 60000 })
export const searchAiReportEvidence = (data) =>
  http.post('/ai-assistant/report-evidence/search', data, { timeout: 60000 })
