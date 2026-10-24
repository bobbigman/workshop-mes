import http from './http'

export const queryPriceLists = params => http.get('/operation-price-list', { params })
export const getPriceList = id => http.get(`/operation-price-list/${id}`)
export const savePriceList = (id, data) => id ? http.put(`/operation-price-list/${id}`, data) : http.post('/operation-price-list', data)
export const previewAttributePrice = data => http.post('/operation-price-list/preview', data)
export const getPricingModes = id => http.get(`/operation-price-list/mode/${id}`)
export const setPricingMode = (product, operation, enabled) => http.put(`/operation-price-list/mode/${product}/${operation}`, enabled, { headers: { 'Content-Type': 'application/json' } })

export const changePriceListStatus = (id, status, expectedVersion) => http.post(`/operation-price-list/${id}/status`, { status, expectedVersion })
export const deletePriceList = (id, expectedVersion) => http.delete(`/operation-price-list/${id}`, { params: { expectedVersion } })
export const getLegacyPriceLists = () => http.get('/operation-price-list/legacy-conversion')
