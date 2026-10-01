import axios from 'axios'
import http, { setSupportProviderName } from './http'

// 【业务背景】登录认证接口，对应后端 AuthController。
export const login = (data) => http.post('/Auth/login', data)
export const loginByPhone = (data) => http.post('/Auth/login-phone', data)
export const changePassword = (data) => http.post('/Auth/change-password', data)
export const getInstanceInfo = () =>
  http.get('/instance-info').then(res => {
    if (res?.code === 0) setSupportProviderName(res.data?.supportProviderName)
    return res
  })
/** 当前工厂授权状态（docs/89） */
export const getLicenseStatus = () => http.get('/license/status')
// 可用账套（工厂）列表：登录页选厂；仅返回 code + name
export const getFactories = () => http.get('/factories')
// 登录页图片（docs/69）：读取 bannerUrl，按账套；上传仅管理员
export const getLoginSetting = (factoryCode) => http.get('/LoginSetting', { params: { factoryCode } })
export const uploadLoginBanner = (formData) => http.post('/LoginSetting/banner', formData)

/**
 * 登录页清缓存专用：独立 Axios，不挂 JWT/401 退出拦截器，禁止带 Authorization。
 * docs/112。
 */
const clearCacheHttp = axios.create({
  baseURL: '/api',
  timeout: 15000,
  headers: {
    'Cache-Control': 'no-cache',
    Pragma: 'no-cache'
  }
})

export async function requestClearBrowserCache() {
  let res
  try {
    res = await clearCacheHttp.post('/Auth/clear-browser-cache', null, {
      // 显式去掉可能被全局默认带上的 Authorization
      transformRequest: [
        (data, headers) => {
          if (headers) {
            delete headers.Authorization
            delete headers.authorization
          }
          return data
        }
      ]
    })
  } catch (err) {
    const status = err.response?.status
    const body = err.response?.data
    const tip =
      (typeof body === 'string' && body.trimStart().startsWith('<')
        ? '接口返回非 JSON（可能路由未注册或静态页兜底）'
        : body?.msg) ||
      err.message ||
      '网络错误'
    const e = new Error(tip)
    e.status = status
    e.url = '/api/Auth/clear-browser-cache'
    e.traceId = err.response?.headers?.['x-trace-id'] || err.response?.headers?.['X-Trace-Id'] || ''
    e.response = err.response
    throw e
  }

  let body = res.data
  if (typeof body === 'string') {
    const raw = body.trimStart()
    if (raw.startsWith('<')) {
      const e = new Error('接口返回非 JSON（可能路由未注册或静态页兜底）')
      e.status = res.status
      e.url = '/api/Auth/clear-browser-cache'
      e.traceId = res.headers?.['x-trace-id'] || res.headers?.['X-Trace-Id'] || ''
      throw e
    }
    try {
      body = JSON.parse(body)
    } catch {
      const e = new Error('接口返回无法解析的文本')
      e.status = res.status
      e.url = '/api/Auth/clear-browser-cache'
      throw e
    }
  }

  return {
    code: body?.code,
    msg: body?.msg,
    data: body?.data,
    status: res.status,
    headers: res.headers
  }
}
