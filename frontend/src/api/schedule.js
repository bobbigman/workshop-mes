import http from './http'

// 排产优先级评分（docs/36 · ①档）
// GET：简易入口（默认权重 + 中性分）
export const getSchedule = (params) => http.get('/Schedule', { params })
// POST：支持手填四维分 + 权重
export const querySchedule = (data) => http.post('/Schedule/query', data)
// 导出 xlsx
export const exportSchedule = (data) => http.post('/Schedule/export', data, { responseType: 'blob' })
