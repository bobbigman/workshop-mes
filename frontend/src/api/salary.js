import http from './http'

export const generateSalary = (data) => http.post('/Salary/generate', data)
export const getSalaryList = (params) => http.get('/Salary', { params })
export const getSalary = (id) => http.get(`/Salary/${id}`)
export const updateSalaryComponents = (id, data) => http.put(`/Salary/${id}/components`, data)
export const confirmSalary = (id) => http.post(`/Salary/${id}/confirm`)
export const recalcSalary = (id) => http.post(`/Salary/${id}/recalc`)
export const revokeSalary = (id) => http.post(`/Salary/${id}/revoke`)
export const deleteSalary = (id) => http.delete(`/Salary/${id}`)
export const exportSalary = (params) => http.get('/Salary/export', { params, responseType: 'blob' })
export const getMyWage = (params) => http.get('/Salary/mine', { params })
export const getSalarySkuSummary = (params) => http.get('/Salary/sku-summary', { params })
export const exportSalarySkuSummary = (params) => http.get('/Salary/sku-summary/export', { params, responseType: 'blob' })
