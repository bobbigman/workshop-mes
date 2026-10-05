/** 登录/侧栏用的实例显示名清洗：去掉英文开发占位，统一产品中文名。 */
import { computed, ref } from 'vue'

export const PRODUCT_TITLE = '微聚'

/** 胡工锚点：代码常量，任何 Instance:CreditPartner 配置都改不了（docs/213） */
export const CREDIT_ANCHOR = '胡工'

/** 版权弹窗独立常量，不与署名行拼接 */
export const CREDIT_RIGHTS = '保留所有权利。'

// 历史产品名：出现时归一化到当前 PRODUCT_TITLE（兼容旧 localStorage 缓存）
const LEGACY_TITLES = ['车间管理系统', '车间小工单系统', '车间小工单', '\u5c0f\u871c\u8702\u62a5\u5de5', '\u5c0f\u871c\u8702']

const PLACEHOLDER_RE = /^(workshop|workshopb)$/i

/** 实例伙伴名；空 → 胡工单方版。由 getInstanceInfo 成功后写入 */
const creditPartner = ref('')
let creditHydrated = false

/**
 * @param {string | null | undefined} raw
 */
export function applyCreditPartner(raw) {
  creditPartner.value = (raw || '').trim()
  creditHydrated = true
}

/** 是否已成功拉过 instance-info（失败未调用则仍为 false，登录弹窗可再拉） */
export function isCreditHydrated() {
  return creditHydrated
}

/**
 * @param {string} partner
 * @returns {string}
 */
export function resolveCreditLine(partner) {
  const p = (partner || '').trim()
  if (!p) return `${PRODUCT_TITLE} · ${CREDIT_ANCHOR}`
  return `${PRODUCT_TITLE} · ${CREDIT_ANCHOR}、${p} 联合出品`
}

/**
 * @param {string} partner
 * @returns {string} 空串表示不渲染角色行
 */
export function resolveCreditRoles(partner) {
  const p = (partner || '').trim()
  if (!p) return ''
  return `产品研发：${CREDIT_ANCHOR}｜客户成功经理：${p}`
}

/** 响应式署名行（模板直接用） */
export const creditLine = computed(() => resolveCreditLine(creditPartner.value))

/** 响应式角色行；空则各页 v-if 隐藏，避免空行 */
export const creditRoles = computed(() => resolveCreditRoles(creditPartner.value))

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
