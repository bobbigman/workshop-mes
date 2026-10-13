import http from './http'

export const getPriceRuleList = (params) => http.get('/PriceRule', { params })
export const createPriceRule = (data) => http.post('/PriceRule', data)
export const updatePriceRule = (id, data) => http.put(`/PriceRule/${id}`, data)
export const deletePriceRule = (id) => http.delete(`/PriceRule/${id}`)
