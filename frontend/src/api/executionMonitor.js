import http from './http'

/** PC 工单执行监控聚合 */
export const getExecutionMonitor = (params) => http.get('/ExecutionMonitor', { params })
