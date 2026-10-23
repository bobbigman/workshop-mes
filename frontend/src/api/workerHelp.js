import http from './http'

export const getWorkerHelpStatus = () => http.get('/worker-help/status')
export const sendWorkerHelpMessage = (data, signal) => http.post('/worker-help/messages', data, { signal, timeout: 120000, silentCancel: true })
export const deleteWorkerHelpConversation = (id) => http.delete(`/worker-help/conversations/${encodeURIComponent(id)}`)
