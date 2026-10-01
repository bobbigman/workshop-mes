/** 登录/侧栏用的实例显示名清洗：去掉英文开发占位，统一产品中文名。 */

export const PRODUCT_TITLE = '小蜜蜂报工'

/** 登录/帮助等页脚署名（联合出品，非法律著作权共有声明） */
export const CREDIT_LINE = '小蜜蜂报工 · 胡工、沈工 联合出品'
export const CREDIT_ROLES = '产品研发：胡工｜客户成功经理：沈工'

// 历史产品名：出现时归一化到当前 PRODUCT_TITLE（兼容旧 localStorage 缓存）
const LEGACY_TITLES = ['车间管理系统', '车间小工单系统', '车间小工单']

const PLACEHOLDER_RE = /^(workshop|workshopb)$/i

function isProductName(name) {
  return !name || PLACEHOLDER_RE.test(name) || name === PRODUCT_TITLE || LEGACY_TITLES.includes(name)
}

/**
 * @param {string | null | undefined} raw
 * @returns {string} 侧栏/localStorage 用的展示名（有效厂名，否则产品名）
 */
export function resolveInstanceLabel(raw) {
  const name = (raw || '').trim()
  return isProductName(name) ? PRODUCT_TITLE : name
}

/**
 * @param {string | null | undefined} raw
 * @returns {string} 非空则作为厂名眉标；空字符串表示不显示眉标
 */
export function resolveFactoryLabel(raw) {
  const name = (raw || '').trim()
  return isProductName(name) ? '' : name
}

/**
 * @param {string | null | undefined} raw
 * @returns {string} document.title
 */
export function resolveDocumentTitle(raw) {
  const factory = resolveFactoryLabel(raw)
  return factory ? `${factory} · ${PRODUCT_TITLE}` : PRODUCT_TITLE
}
