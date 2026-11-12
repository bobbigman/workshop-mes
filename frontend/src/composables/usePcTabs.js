import { ref, reactive } from 'vue'

/** @typedef {{ path: string, title: string }} PcTab */

const visited = ref(/** @type {PcTab[]} */ ([]))
/** path → epoch；关闭页签时 +1，使 keep-alive key 失效 */
const epochs = reactive(/** @type {Record<string, number>} */ ({}))

/**
 * 布局内进入子路由时登记页签（按 path 去重；由 layout 调用）。
 * @param {import('vue-router').RouteLocationNormalizedLoaded} route
 */
export function addPcTab(route) {
  if (!route || route.path === '/login') return
  const title = route.meta?.title
  if (!title || typeof title !== 'string') return
  const path = route.path
  const hit = visited.value.find((t) => t.path === path)
  if (hit) {
    // 单据页可能已 setPcTabTitle 成工单号/单据号，勿被 meta 默认标题盖回
    if (!route.meta?.billTab) hit.title = title
    return
  }
  visited.value.push({ path, title })
}

/**
 * 单据打开后把页签标题改成工单号/单据号（path 不变）。
 * @param {string} path
 * @param {string} title
 */
export function setPcTabTitle(path, title) {
  if (!path || !title) return
  const hit = visited.value.find((t) => t.path === path)
  if (hit) {
    hit.title = title
    return
  }
  visited.value.push({ path, title })
}

/**
 * @param {string} path
 * @param {string} [activePath] 当前路由 path
 * @returns {string | null} 关闭后应跳转的 path；null 表示不跳或未关
 */
export function closePcTab(path, activePath) {
  const list = visited.value
  const idx = list.findIndex((t) => t.path === path)
  if (idx < 0) return null
  // 只剩首页时不可关，避免主区空白
  if (list.length === 1 && list[0].path === '/home') return null
  list.splice(idx, 1)
  epochs[path] = (epochs[path] || 0) + 1
  if (list.length === 0) return '/home'
  if (activePath !== path) return null
  const next = list[idx] || list[idx - 1]
  return next?.path ?? null
}

export function clearPcTabs() {
  visited.value = []
  for (const k of Object.keys(epochs)) delete epochs[k]
}

export function pcTabCacheKey(path) {
  return `${path}@${epochs[path] || 0}`
}

export function usePcTabs() {
  return { visited, epochs, addPcTab, setPcTabTitle, closePcTab, clearPcTabs, pcTabCacheKey }
}
