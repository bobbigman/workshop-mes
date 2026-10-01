import axios from 'axios'
import { ElMessage, ElMessageBox } from 'element-plus'

// 【业务背景】统一请求封装：所有接口返回 {code,msg,data}；失败绝不吞，必须把 msg 抛给调用方。
// 技术故障 msg 含 SQL/报文（后端已脱敏）；长文案用弹窗便于复制查看。追踪号在响应头 X-Trace-Id。
// docs/111：空 msg 提示请洽软件提供商 + 追踪号，禁止笼统无信息量兜底。
const http = axios.create({
  baseURL: '/api',
  timeout: 15000
})

const PROVIDER_KEY = 'supportProviderName'

/** 登录/布局拉 instance-info 后写入；空则提示「您的软件提供商」 */
export function setSupportProviderName(name) {
  const v = (name || '').trim()
  if (v) localStorage.setItem(PROVIDER_KEY, v)
  else localStorage.removeItem(PROVIDER_KEY)
}

function providerLabel() {
  return (localStorage.getItem(PROVIDER_KEY) || '').trim() || '您的软件提供商'
}

function traceIdOf(headers) {
  return headers?.['x-trace-id'] || headers?.['X-Trace-Id'] || ''
}

function traceSuffix(headers) {
  const id = traceIdOf(headers)
  return id ? `，追踪号 ${id}` : ''
}

/** msg 为空时的可排查提示（docs/111 拍板：仅空 msg 走此模板） */
function fallbackFailTip(headers) {
  return `请求失败，请洽${providerLabel()}，错误信息：服务端未返回详细原因${traceSuffix(headers)}`
}

function withTraceIfLong(tip, headers) {
  if (!tip) return fallbackFailTip(headers)
  const long =
    tip.length > 80 ||
    /SQL原文|SQL执行失败|调用API失败|← /.test(tip)
  if (!long) return tip
  if (/追踪号/.test(tip)) return tip
  return `${tip}${traceSuffix(headers)}`
}

function showApiError(tip) {
  const text = tip || fallbackFailTip()
  const long =
    text.length > 80 ||
    /SQL原文|SQL执行失败|调用API失败|← /.test(text)
  if (long) {
    ElMessageBox.alert(text, '错误详情', {
      confirmButtonText: '知道了',
      customClass: 'mes-error-detail',
      dangerouslyUseHTMLString: false
    }).catch(() => {})
  } else {
    ElMessage.error(text)
  }
}

// 请求拦截：带 JWT
http.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  // API 必须实时取数：旧路由曾返回可缓存的首页 HTML，刷新页面也可能继续命中它。
  if (config.method?.toLowerCase() === 'get') {
    config.headers['Cache-Control'] = 'no-cache'
    config.headers.Pragma = 'no-cache'
  }
  return config
})

http.interceptors.response.use(
  res => {
    if (res.config.responseType === 'blob') return res
    let body = res.data
    // axios 偶发未解析 JSON（Content-Type 异常）时 body 是字符串；仅 HTML 才判「路由兜底」
    if (typeof body === 'string') {
      const raw = body.trimStart()
      if (raw.startsWith('<')) {
        const tip = `接口返回非 JSON（可能路由未注册或静态页兜底）${traceSuffix(res.headers)}`
        showApiError(tip)
        return Promise.reject(new Error(tip))
      }
      try {
        body = JSON.parse(body)
      } catch {
        const tip = `接口返回无法解析的文本${traceSuffix(res.headers)}`
        showApiError(tip)
        return Promise.reject(new Error(tip))
      }
    }
    if (body && body.code !== 0) {
      const tip = body.msg
        ? withTraceIfLong(body.msg, res.headers)
        : fallbackFailTip(res.headers)
      showApiError(tip)
      return Promise.reject(new Error(tip))
    }
    return body
  },
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      localStorage.removeItem('userName')
      localStorage.removeItem('role')
      localStorage.removeItem('licenseTier')
      localStorage.removeItem('licenseStatus')
      localStorage.removeItem('licenseMessage')
      if (!window.location.hash.startsWith('#/login')) {
        ElMessage.warning('登录已失效，请重新登录')
        const hashPath = window.location.hash.slice(1) || '/'
        const loginHash =
          hashPath.startsWith('/h5/') && !hashPath.startsWith('//')
            ? `#/login?redirect=${encodeURIComponent(hashPath)}`
            : '#/login'
        window.location.hash = loginHash
      }
      return Promise.reject(err)
    }
    const data = err.response?.data
    const headers = err.response?.headers
    let tip = ''
    if (data && typeof data === 'object' && data.msg) tip = withTraceIfLong(data.msg, headers)
    else if (typeof data === 'string' && data) {
      // HTML 兜底页太长且无信息量，改成短提示 + 追踪号
      tip = data.trimStart().startsWith('<')
        ? `接口返回非 JSON（可能路由未注册或静态页兜底）${traceSuffix(headers)}`
        : withTraceIfLong(data, headers)
    } else tip = err.message || `网络或服务器异常${traceSuffix(headers)}`
    if (!tip) tip = fallbackFailTip(headers)
    showApiError(tip)
    return Promise.reject(new Error(tip))
  }
)

export default http
