import http from './http'

export const searchQuoteProducts = (params) => http.get('/QuoteSuggest/products', { params })
export const probeKingdeeMaterial = (code) => http.get('/QuoteSuggest/kingdee/material', { params: { code } })
export const previewQuoteSuggest = (data) => http.post('/QuoteSuggest/preview', data)
export const exportQuoteSuggest = (data) =>
  http.post('/QuoteSuggest/export', data, { responseType: 'blob' })
