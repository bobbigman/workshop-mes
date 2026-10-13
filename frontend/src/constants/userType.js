/** 人员类型（docs/161）：0内部 / 1临时 / 2外部 */
export const USER_TYPE_OPTIONS = [
  { value: 0, label: '内部员工' },
  { value: 1, label: '临时用工' },
  { value: 2, label: '外部供应商' }
]

export function userTypeLabel(t) {
  if (t === 1) return '临时用工'
  if (t === 2) return '外部供应商'
  return '内部员工'
}

/** Element Plus Tag type */
export function userTypeTagType(t) {
  if (t === 1) return 'warning'
  if (t === 2) return 'danger'
  return 'info'
}
