/**
 * 登录页「清缓存并刷新」工具（docs/112）。
 * 只清本站 HTTP 缓存指令 + mes-* CacheStorage + 本系统 /sw.js；保留 localStorage 登录信息。
 */

export const FORM_RESTORE_KEY = 'mes.cacheRefresh.form'
export const REFRESH_QUERY = '_mes_cache_refresh'
const FORM_MAX_AGE_MS = 10 * 60 * 1000
const ACCOUNT_MAX_LEN = 64
const FACTORY_CODE_MAX_LEN = 64

/**
 * @param {object} [opts]
 * @param {() => Promise<{ code: number, msg?: string, data?: any, headers?: any, status?: number }>} opts.requestClear
 * @param {Window} [opts.win]
 * @returns {Promise<{
 *   ok: boolean,
 *   limited: boolean,
 *   message: string,
 *   step?: string,
 *   error?: string
 * }>}
 */
export async function runClearBrowserCache(opts = {}) {
  const win = opts.win || (typeof window !== 'undefined' ? window : null)
  if (!win) {
    return { ok: false, limited: false, message: '清缓存失败：无浏览器环境', step: 'env', error: 'no window' }
  }

  const limitedReasons = []
  if (!win.isSecureContext) {
    limitedReasons.push('当前不是安全上下文，浏览器可能无法执行完整的缓存清理指令')
  }

  // 1) 同源匿名 POST，触发 Clear-Site-Data: "cache"
  if (typeof opts.requestClear !== 'function') {
    return {
      ok: false,
      limited: false,
      message: '清缓存接口失败：未配置请求',
      step: 'api',
      error: 'requestClear missing'
    }
  }
  try {
    const res = await opts.requestClear()
    if (!res || res.code !== 0) {
      const tip = res?.msg || '业务返回失败'
      return {
        ok: false,
        limited: false,
        message: `清缓存接口失败：${tip}`,
        step: 'api',
        error: tip
      }
    }
  } catch (err) {
    const tip = formatRequestError(err)
    return {
      ok: false,
      limited: false,
      message: `清缓存接口失败：${tip}`,
      step: 'api',
      error: tip
    }
  }

  // 2) 注销本系统 Service Worker
  try {
    const swResult = await unregisterMesServiceWorkers(win)
    if (swResult.unsupported) {
      limitedReasons.push('当前环境不支持注销本页脚本缓存')
    }
  } catch (err) {
    return {
      ok: false,
      limited: false,
      message: `注销本页脚本缓存失败：${err?.message || String(err)}`,
      step: 'service-worker',
      error: err?.message || String(err)
    }
  }

  // 3) 删除 mes-* CacheStorage
  try {
    const cacheResult = await deleteMesCaches(win)
    if (cacheResult.unsupported) {
      limitedReasons.push('当前环境不支持清理本地资源缓存')
    }
  } catch (err) {
    return {
      ok: false,
      limited: false,
      message: `清理本地资源缓存失败：${err?.message || String(err)}`,
      step: 'cache-storage',
      error: err?.message || String(err)
    }
  }

  if (limitedReasons.length) {
    return {
      ok: true,
      limited: true,
      message: `此环境无法完整自动清理缓存，可刷新后重试；登录信息保留。${limitedReasons[0]}`
    }
  }

  return {
    ok: true,
    limited: false,
    message: '缓存清理请求已完成，正在刷新；登录信息保留'
  }
}

/** 仅注销同源 /sw.js 且 scope 为本应用根路径的注册 */
export async function unregisterMesServiceWorkers(win = window) {
  if (!('serviceWorker' in win.navigator)) {
    return { unsupported: true, unregistered: 0 }
  }
  const regs = await win.navigator.serviceWorker.getRegistrations()
  const rootScope = new URL('/', win.location.origin).href
  let unregistered = 0
  for (const reg of regs) {
    const scriptURL =
      reg.active?.scriptURL ||
      reg.waiting?.scriptURL ||
      reg.installing?.scriptURL ||
      ''
    if (!scriptURL) continue
    let pathname = ''
    try {
      pathname = new URL(scriptURL).pathname
    } catch {
      continue
    }
    if (pathname !== '/sw.js') continue
    if (reg.scope !== rootScope) continue
    const ok = await reg.unregister()
    if (ok === false) {
      // 已被其它操作移除，视为当前不存在
      continue
    }
    unregistered += 1
  }
  return { unsupported: false, unregistered }
}

/** 仅删除当前 origin 下名称以 mes- 开头的 CacheStorage */
export async function deleteMesCaches(win = window) {
  if (!win.caches || typeof win.caches.keys !== 'function') {
    return { unsupported: true, deleted: [] }
  }
  const keys = await win.caches.keys()
  const targets = keys.filter((k) => typeof k === 'string' && k.startsWith('mes-'))
  const deleted = []
  for (const name of targets) {
    const ok = await win.caches.delete(name)
    if (ok) deleted.push(name)
  }
  return { unsupported: false, deleted }
}

/** 保存非敏感表单字段到一次性 sessionStorage（禁止存密码/Token） */
export function saveFormForRestore(account, factoryCode, storage) {
  const store = storage || (typeof sessionStorage !== 'undefined' ? sessionStorage : null)
  if (!store) return
  const payload = {
    account: String(account || '').slice(0, ACCOUNT_MAX_LEN),
    factoryCode: String(factoryCode || '').slice(0, FACTORY_CODE_MAX_LEN),
    savedAt: Date.now()
  }
  store.setItem(FORM_RESTORE_KEY, JSON.stringify(payload))
}

/**
 * 读取并删除一次性恢复键；过期/异常返回 null。
 * @returns {{ account: string, factoryCode: string } | null}
 */
export function consumeFormRestore(storage) {
  const store = storage || (typeof sessionStorage !== 'undefined' ? sessionStorage : null)
  if (!store) return null
  const raw = store.getItem(FORM_RESTORE_KEY)
  if (!raw) return null
  store.removeItem(FORM_RESTORE_KEY)
  try {
    const data = JSON.parse(raw)
    if (!data || typeof data !== 'object') return null
    if (typeof data.savedAt !== 'number' || Date.now() - data.savedAt > FORM_MAX_AGE_MS) return null
    const account = typeof data.account === 'string' ? data.account.slice(0, ACCOUNT_MAX_LEN) : ''
    const factoryCode =
      typeof data.factoryCode === 'string' ? data.factoryCode.slice(0, FACTORY_CODE_MAX_LEN) : ''
    return { account, factoryCode }
  } catch {
    return null
  }
}

/** 在 hash 前增加一次性刷新参数，避开旧首页 HTTP 缓存键 */
export function buildRefreshUrl(href) {
  const u = new URL(href || (typeof location !== 'undefined' ? location.href : 'http://local/'))
  u.searchParams.set(REFRESH_QUERY, `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`)
  return u.toString()
}

/** 刷新后去掉已使用的刷新参数，不再次导航 */
export function stripRefreshParam(win = typeof window !== 'undefined' ? window : null) {
  if (!win?.location || !win.history?.replaceState) return false
  const u = new URL(win.location.href)
  if (!u.searchParams.has(REFRESH_QUERY)) return false
  u.searchParams.delete(REFRESH_QUERY)
  const next = `${u.pathname}${u.search}${u.hash}`
  win.history.replaceState(win.history.state, '', next)
  return true
}

function formatRequestError(err) {
  if (!err) return '未知错误'
  const status = err.status || err.response?.status
  const trace =
    err.traceId ||
    err.response?.headers?.['x-trace-id'] ||
    err.response?.headers?.['X-Trace-Id'] ||
    ''
  const url = err.url || err.config?.url || '/api/Auth/clear-browser-cache'
  const parts = []
  if (url) parts.push(url)
  if (status) parts.push(`HTTP ${status}`)
  if (trace) parts.push(`追踪号 ${trace}`)
  const msg = err.message || String(err)
  if (msg && !parts.includes(msg)) parts.push(msg)
  return parts.join('，')
}
