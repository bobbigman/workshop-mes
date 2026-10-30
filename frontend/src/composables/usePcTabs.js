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
    if (hit.title !== title) hit.title = title
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
  if (list.length <= 1) return null
  const idx = list.findIndex((t) => t.path === path)
  if (idx < 0) return null
  list.splice(idx, 1)
  epochs[path] = (epochs[path] || 0) + 1
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
  return { visited, epochs, addPcTab, closePcTab, clearPcTabs, pcTabCacheKey }
}
