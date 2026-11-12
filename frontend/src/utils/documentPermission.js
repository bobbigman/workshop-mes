/** 清单 #24：单据权限（仅创建人可改）；管理员仍可改全部 */

export function currentUserId() {
  const n = Number(localStorage.getItem('userId') || 0)
  return Number.isFinite(n) && n > 0 ? n : 0
}

export function isAdminRole(role = Number(localStorage.getItem('role') || 0)) {
  return Number(role) === 1
}

/**
 * 开关关 / 管理员 / 本人创建 → 可改；开关开且非管理员且非创建人 → 不可改。
 * createdBy 空（旧数据）时：开关开则仅管理员可改。
 */
export function canEditByCreator(onlyCreatorOn, createdBy, role) {
  if (!onlyCreatorOn) return true
  if (isAdminRole(role)) return true
  const uid = currentUserId()
  const cb = createdBy == null || createdBy === '' ? null : Number(createdBy)
  return uid > 0 && cb != null && Number.isFinite(cb) && cb === uid
}
