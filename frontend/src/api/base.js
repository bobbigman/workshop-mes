import http from './http'

// 【业务背景】基础数据：单位 / 不良品项 / 工序 / 工艺路线 / 产品
export const getUnitList = () => http.get('/Unit')
export const createUnit = (data) => http.post('/Unit', data)
export const updateUnit = (id, data) => http.put(`/Unit/${id}`, data)
export const deleteUnit = (id) => http.delete(`/Unit/${id}`)

export const getDefectList = () => http.get('/DefectItem')
export const createDefect = (data) => http.post('/DefectItem', data)
export const updateDefect = (id, data) => http.put(`/DefectItem/${id}`, data)
export const deleteDefect = (id) => http.delete(`/DefectItem/${id}`)

export const getOperationList = (params) => http.get('/Operation', { params })
export const getOperation = (id) => http.get(`/Operation/${id}`)
export const createOperation = (data) => http.post('/Operation', data)
export const updateOperation = (id, data) => http.put(`/Operation/${id}`, data)
export const deleteOperation = (id) => http.delete(`/Operation/${id}`)

export const getRoutingList = (params) => http.get('/Routing', { params })
export const getRouting = (id) => http.get(`/Routing/${id}`)
export const createRouting = (data) => http.post('/Routing', data)
export const updateRouting = (id, data) => http.put(`/Routing/${id}`, data)
export const deleteRouting = (id) => http.delete(`/Routing/${id}`)

export const getProductList = (params) => http.get('/Product', { params })
export const getProduct = (id) => http.get(`/Product/${id}`)
export const createProduct = (data) => http.post('/Product', data)
export const updateProduct = (id, data) => http.put(`/Product/${id}`, data)
export const deleteProduct = (id) => http.delete(`/Product/${id}`)
