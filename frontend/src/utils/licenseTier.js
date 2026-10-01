/** 与后端 LicenseTierPolicy 对齐（docs/88） */

export const TIER = {
  trial: 'trial',
  enterprise: 'enterprise',
  flagship: 'flagship'
}

export const FEATURE = {
  PieceWage: 'PieceWage',
  PrintLabel: 'PrintLabel',
  ScanReport: 'ScanReport',
  WechatDueAlert: 'WechatDueAlert',
  ReportReview: 'ReportReview',
  MultiWorkshop: 'MultiWorkshop',
  KingdeeMcp: 'KingdeeMcp',
  DataScreen: 'DataScreen'
}

const RANK = {
  trial: 0,
  enterprise: 1,
  flagship: 2
}

/** 功能 → 最低档 */
const MIN_TIER = {
  [FEATURE.PieceWage]: TIER.enterprise,
  [FEATURE.PrintLabel]: TIER.enterprise,
  [FEATURE.ScanReport]: TIER.enterprise,
  [FEATURE.WechatDueAlert]: TIER.enterprise,
  [FEATURE.ReportReview]: TIER.enterprise,
  [FEATURE.MultiWorkshop]: TIER.flagship,
  [FEATURE.KingdeeMcp]: TIER.flagship,
  [FEATURE.DataScreen]: TIER.flagship
}

export function normalizeTier(raw) {
  const t = String(raw || '').trim().toLowerCase()
  if (t === TIER.enterprise || t === TIER.flagship) return t
  return TIER.trial
}

export function currentTier() {
  return normalizeTier(localStorage.getItem('licenseTier'))
}

export function canUse(tierOrNull, feature) {
  const tier = normalizeTier(tierOrNull ?? currentTier())
  const need = MIN_TIER[feature]
  if (!need) return true
  return (RANK[tier] ?? 0) >= (RANK[need] ?? 0)
}
