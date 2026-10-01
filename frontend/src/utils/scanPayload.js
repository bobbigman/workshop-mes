/** 扫码/PDA 共用：从纯工单号或本站报工链接提取 orderNo（可选 op=工序ID）。 */

export const ORDER_NO_MAX_LEN = 64

/**
 * @param {unknown} raw
 * @returns {{ ok: true, orderNo: string } | { ok: false, error: string }}
 */
export function parseOrderNo(raw) {
  const orderNo = String(raw ?? '').trim()
  if (!orderNo) return { ok: false, error: '未识别到工单号' }
  if (orderNo.length > ORDER_NO_MAX_LEN) return { ok: false, error: '工单号过长' }
  return { ok: true, orderNo }
}

/**
 * @param {string} raw
 * @returns {number | undefined} 合法正整数工序 ID；非法则忽略
 */
function parseOptionalOp(raw) {
  if (raw == null || raw === '') return undefined
  const n = Number(raw)
  if (!Number.isInteger(n) || n <= 0) return undefined
  return n
}

/**
 * @param {string} raw
 * @param {{ origin?: string }} [options]
 * @returns {{ ok: true, orderNo: string, operationId?: number } | { ok: false, error: string }}
 */
function parseReportUrl(raw, options = {}) {
  let url
  try {
    url = new URL(raw)
  } catch {
    return { ok: false, error: '无效的二维码内容' }
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    return { ok: false, error: '不支持的链接协议' }
  }
  if (url.username || url.password) {
    return { ok: false, error: '无效的报工链接' }
  }

  const expectedOrigin = options.origin || (typeof window !== 'undefined' ? window.location.origin : '')
  if (!expectedOrigin || url.origin !== expectedOrigin) {
    return { ok: false, error: '请扫描本系统工单二维码' }
  }

  const hash = url.hash || ''
  if (!hash.startsWith('#')) {
    return { ok: false, error: '请扫描本系统工单二维码' }
  }

  const hashBody = hash.slice(1) // /h5/report?order=...
  const qIndex = hashBody.indexOf('?')
  const path = qIndex >= 0 ? hashBody.slice(0, qIndex) : hashBody
  const query = qIndex >= 0 ? hashBody.slice(qIndex + 1) : ''

  if (path !== '/h5/report') {
    return { ok: false, error: '请扫描本系统工单二维码' }
  }

  const params = new URLSearchParams(query)
  const orders = params.getAll('order')
  if (orders.length === 0) {
    return { ok: false, error: '链接缺少工单号' }
  }
  if (orders.length > 1) {
    return { ok: false, error: '链接工单号无效' }
  }

  const parsed = parseOrderNo(orders[0])
  if (!parsed.ok) return parsed

  const ops = params.getAll('op')
  // 多个 op 或非法 → 忽略 op，仍可按工单号报工（向后兼容、不阻断）
  if (ops.length === 1) {
    const operationId = parseOptionalOp(ops[0])
    if (operationId != null) return { ok: true, orderNo: parsed.orderNo, operationId }
  }
  return { ok: true, orderNo: parsed.orderNo }
}

/**
 * 摄像头 / PDA 扫描结果解析。手动输入请用 parseOrderNo。
 * @param {unknown} raw
 * @param {{ origin?: string }} [options]
 * @returns {{ ok: true, orderNo: string, operationId?: number } | { ok: false, error: string }}
 */
export function parseScanPayload(raw, options = {}) {
  const text = String(raw ?? '').trim()
  if (!text) return { ok: false, error: '未识别到工单号' }

  if (/^[a-z][a-z0-9+.-]*:/i.test(text) || text.startsWith('//')) {
    return parseReportUrl(text, options)
  }
  return parseOrderNo(text)
}
