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

/** 登录/布局拉 instance-info 后写入；空则提示「请在系统设置中填写软件提供商名称，以便报错时用户能联系到正确的技术支持。」 */
export function setSupportProviderName(name) {
  const v = (name || '').trim()
  if (v) localStorage.setItem(PROVIDER_KEY, v)
  else localStorage.removeItem(PROVIDER_KEY)
}

function providerLabel() {
  return (localStorage.getItem(PROVIDER_KEY) || '').trim() || '请在系统设置中填写软件提供商名称，以便报错时用户能联系到正确的技术支持。'
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
  return `请求失败，请联系${providerLabel()}；服务端未返回详细原因${traceSuffix(headers)}`
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
  } else if (text.includes('\n')) {
    // 两行大白话：主句 + 状态码细节
    ElMessage({
      type: 'error',
      duration: 5000,
      dangerouslyUseHTMLString: true,
      message: text.split('\n').map(escapeHtml).join('<br/>')
    })
  } else {
    ElMessage.error(text)
  }
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** axios 传输层失败（无业务 msg）：改成大白话，避免英文 Request failed… */
function transportFailTip(err) {
  const status = err.response?.status
  if (!err.response) {
    if (err.code === 'ECONNABORTED' || /timeout/i.test(err.message || '')) {
      return '等太久没有回应，请稍后再试。\n（请求超时）'
    }
    return '连不上后台了，可能正在重启，请稍后再试。\n（网络未连通）'
  }
  if (status === 500) return '后台出了点问题，请稍后再试。\n（服务器错误 500）'
  if (status === 502 || status === 503 || status === 504) {
    return `后台暂时不可用，可能正在重启，请稍后再试。\n（服务不可用 ${status}）`
  }
  if (status >= 400) return `请求没成功，请稍后再试。\n（错误 ${status}）`
  return err.message || '无法连接到服务器，请检查服务器是否在线。'
}

/** 登录/选厂/个人码：不带旧 Bearer，避免选厂成功后 identity 校验与并发 401 清 token 互相踩 */
function isAnonymousAuthRequest(url) {
  const u = String(url || '')
  return (
    /\/Auth\/login$/i.test(u) ||
    /\/Auth\/select-factory$/i.test(u) ||
    /\/Auth\/login-by-qr$/i.test(u) ||
    /\/Auth\/login-phone$/i.test(u)
  )
}

// 请求拦截：带 JWT
http.interceptors.request.use(config => {
  const skipBearer = isAnonymousAuthRequest(config.url)
  const token = skipBearer ? null : localStorage.getItem('token')
  config.identityToken = token
  if (token) config.headers.Authorization = `Bearer ${token}`
  else if (skipBearer && config.headers) {
    delete config.headers.Authorization
  }
  // API 必须实时取数：旧路由曾返回可缓存的首页 HTML，刷新页面也可能继续命中它。
  if (config.method?.toLowerCase() === 'get') {
    config.headers['Cache-Control'] = 'no-cache'
    config.headers.Pragma = 'no-cache'
  }
  return config
})

http.interceptors.response.use(
  res => {
    // 登录/选厂等匿名接口故意不带 Bearer（identityToken=null），登录页常仍留着旧 JWT；
    // 若按「身份已变」丢弃，会静默失败、登录按钮像没反应。
    if (
      !isAnonymousAuthRequest(res.config.url) &&
      res.config.identityToken !== localStorage.getItem('token')
    ) {
      return Promise.reject(Object.assign(new Error('登录身份已变化，旧请求结果已丢弃。'), { code: 'ERR_IDENTITY_CHANGED' }))
    }
    if (res.config.responseType === 'blob') return res
    let body = res.data
    // axios 偶发未解析 JSON（Content-Type 异常）时 body 是字符串；仅 HTML 才判「路由兜底」
    if (typeof body === 'string') {
      const raw = body.trimStart()
      if (raw.startsWith('<')) {
        const tip = `请求返回内容异常${traceSuffix(res.headers)}，请检查服务器路由设置和转发规则。`
        showApiError(tip)
        return Promise.reject(Object.assign(new Error(tip), { response: res }))
      }
      try {
        body = JSON.parse(body)
      } catch {
        const tip = `返回内容无法识别${traceSuffix(res.headers)}，请检查后端服务是否正常。`
        showApiError(tip)
        return Promise.reject(Object.assign(new Error(tip), { response: res }))
      }
    }
    if (body && body.code !== 0) {
      // docs/150：报工上级 soft 确认，不弹错误，交给页面二次确认后带 confirmUpstream 重提
      if (body.code === 1002) {
        const err = new Error(body.msg || '上级工序未达上限，是否继续？')
        err.code = 1002
        err.softConfirm = true
        return Promise.reject(err)
      }
      const tip = body.msg
        ? withTraceIfLong(body.msg, res.headers)
        : fallbackFailTip(res.headers)
      showApiError(tip)
      const err = Object.assign(new Error(tip), { response: res, data: body.data })
      if (body.data != null && body.data.fieldId != null) err.fieldId = body.data.fieldId
      return Promise.reject(err)
    }
    return body
  },
  err => {
    if (
      err.config &&
      !isAnonymousAuthRequest(err.config.url) &&
      err.config.identityToken !== localStorage.getItem('token')
    ) {
      return Promise.reject(Object.assign(new Error('登录身份已变化，旧请求结果已丢弃。'), { code: 'ERR_IDENTITY_CHANGED' }))
    }
    // 工人帮助主动取消由页面显示中文反馈；仍将取消抛回调用方。
    if (err.code === 'ERR_CANCELED' && err.config?.silentCancel) return Promise.reject(err)
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
      localStorage.removeItem('userName')
      localStorage.removeItem('role')
      localStorage.removeItem('licenseTier')
      localStorage.removeItem('licenseStatus')
      localStorage.removeItem('licenseMessage')
      window.dispatchEvent(new Event('mes-auth-changed'))
      if (!window.location.hash.startsWith('#/login')) {
        ElMessage.warning('登录凭证已失效，请重新登录。')
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
        ? `请求返回内容异常${traceSuffix(headers)}，请检查服务器路由设置和转发规则。`
        : withTraceIfLong(data, headers)
    } else if (/^Request failed with status code \d+$/i.test(err.message || '') || !data) {
      tip = transportFailTip(err)
    } else tip = err.message || `无法连接到服务器${traceSuffix(headers)}，请检查服务器是否在线、IP和端口是否正确、防火墙是否放行。`
    if (!tip) tip = fallbackFailTip(headers)
    showApiError(tip)
    return Promise.reject(Object.assign(new Error(tip), { response: err.response, code: err.code }))
  }
)

export default http
