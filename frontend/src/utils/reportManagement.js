/** 入口检查只决定显示/导航；业务权限仍由原接口校验。 */
export function canOpenReportManagement(action, role) {
  const n = Number(role)
  return action === 'proxy' ? n === 1 || n === 3 : ['batch', 'pending'].includes(action) && n === 1
}

export function isReportManagementEntry(query) {
  return query.from === 'h5' && Object.prototype.hasOwnProperty.call(query, 'action')
}

export function managementDestination(action) {
  return { path: '/report', query: { from: 'h5', action } }
}

export function updateManagementTime(range, index, value) {
  const next = [...(range || ['', ''])]
  next[index] = value && value.length === 16 ? value + ':00' : value
  return next.some(Boolean) ? next : null
}

export function managementTimeError(range) {
  if (!range?.some(Boolean)) return ''
  const [start, end] = range
  if (!start || !end || !Number.isFinite(Date.parse(start)) || !Number.isFinite(Date.parse(end)) || Date.parse(end) <= Date.parse(start)) return '请补全起止时间，结束须晚于开始'
  if (Date.parse(end) - Date.parse(start) > 86400000) return '单次计时不能超过24小时'
  return ''
}
