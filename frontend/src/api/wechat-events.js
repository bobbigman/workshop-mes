import request from './http'

export const getWechatRules = () => request.get('/WechatAlert/rules')
export const createWechatRule = data => request.post('/WechatAlert/rules', data)
export const updateWechatRule = (id, data) => request.put(`/WechatAlert/rules/${id}`, data)
export const getWechatDeliveries = params => request.get('/WechatAlert/deliveries', { params })
export const getWechatAttempts = id => request.get(`/WechatAlert/deliveries/${id}/attempts`)
export const retryWechatDelivery = id => request.post(`/WechatAlert/deliveries/${id}/retry`)
export const verifyWechatDelivery = (id, data) => request.post(`/WechatAlert/deliveries/${id}/verify`, data)
