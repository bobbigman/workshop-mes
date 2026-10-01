import http from './http'

// 【业务背景】工单 / 报工 / 报表 / 看板 / 自定义字段 / 导入
export const getOrderList = (params) => http.get('/WorkOrder', { params })
export const getOrderStatusCounts = () => http.get('/WorkOrder/status-counts')
export const getOrder = (id) => http.get(`/WorkOrder/${id}`)
export const getOrderByNo = (orderNo) => http.get('/WorkOrder/by-no', { params: { orderNo } })
export const createOrder = (data) => http.post('/WorkOrder', data)
export const updateOrder = (id, data) => http.put(`/WorkOrder/${id}`, data)
export const transitionOrder = (id, action) => http.post(`/WorkOrder/${id}/transition`, { action })
export const copyOrder = (id) => http.post(`/WorkOrder/${id}/copy`)
export const deleteOrder = (id) => http.delete(`/WorkOrder/${id}`)
export const getOrderQr = (id) => http.get(`/WorkOrder/${id}/qr`)

export const submitReport = (data) => http.post('/Report', data)
export const batchReport = (data) => http.post('/Report/batch', data)
export const updateReport = (id, data) => http.put(`/Report/${id}`, data)
export const getReportList = (params) => http.get('/Report', { params })
export const getMyReports = (params) => http.get('/Report/mine', { params })
export const getReportCandidates = (operationId) => http.get(`/Report/candidates/${operationId}`)
export const getDefectsByOp = (operationId) => http.get(`/Report/defects/${operationId}`)

export const getProductionStat = (params) => http.get('/ReportStat/production', { params })
export const getSkuSummary = (params) => http.get('/ReportStat/sku-summary', { params })
export const getBoard = () => http.get('/ReportStat/board')

export const getAbnormalList = (params) => http.get('/Abnormal', { params })
export const createAbnormal = (formData) => http.post('/Abnormal', formData)
export const resolveAbnormal = (id, data) => http.post(`/Abnormal/${id}/resolve`, data)

export const getCustomFields = (target) => http.get('/CustomField', { params: { target } })
export const createCustomField = (data) => http.post('/CustomField', data)
export const updateCustomField = (id, data) => http.put(`/CustomField/${id}`, data)
export const deleteCustomField = (id) => http.delete(`/CustomField/${id}`)

export const getPrintSetting = () => http.get('/PrintSetting')
export const savePrintSetting = (data) => http.put('/PrintSetting', data)

export const getWechatAlertSetting = () => http.get('/WechatAlert/setting')
export const saveWechatAlertSetting = (data) => http.put('/WechatAlert/setting', data)
export const markWechatNoticeSeen = () => http.post('/WechatAlert/notice-seen')
export const testWechatAlert = () => http.post('/WechatAlert/test')
export const pushWechatOverdue = () => http.post('/WechatAlert/push-overdue')

export const downloadTemplate = (entityType) => http.get('/Import/template', { params: { entityType }, responseType: 'blob' })
export const uploadImport = (entityType, formData) => http.post(`/Import/upload?entityType=${entityType}`, formData)
