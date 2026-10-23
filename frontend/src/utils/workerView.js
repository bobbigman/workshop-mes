/**
 * 工人手机视角（docs/200）
 * mode: 1=全车间可见 2=只看我的任务；仅 role=2 生效。
 */
const STORAGE_KEY = 'workerViewMode'

export function setWorkerViewMode(mode) {
  const n = Number(mode) === 2 ? 2 : 1
  localStorage.setItem(STORAGE_KEY, String(n))
  return n
}

export function getStoredWorkerViewMode() {
  return Number(localStorage.getItem(STORAGE_KEY) || 1) === 2 ? 2 : 1
}

/** 生产人员 + mode=2 → 藏工单入口；默认入口统一为工作台。 */
export function isWorkerTasksOnlyView() {
  const role = Number(localStorage.getItem('role') || 0)
  return role === 2 && getStoredWorkerViewMode() === 2
}

export function workerHomePath() {
  return '/h5/home'
}
