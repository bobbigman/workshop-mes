import http from './http'

export const startWorkSession = (data) => http.post('/work-session/start', data)
export const getCurrentWorkSession = (params) => http.get('/work-session/current', { params })
export const getPendingWorkSessions = () => http.get('/work-session/pending')
export const reportPendingWorkSession = (id, data) => http.post(`/work-session/${id}/report`, data)
