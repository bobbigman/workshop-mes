import { ref } from 'vue'
import { getFavorites, toggleFavorite } from '@/api/userFavorite'

const list = ref([])
const loaded = ref(false)
const loadError = ref(false)
let req = 0
let inflight = null

export function resetFavorites() {
  req += 1
  list.value = []
  loaded.value = false
  loadError.value = false
  inflight = null
}

async function refresh() {
  const id = ++req
  try {
    const res = await getFavorites()
    if (id !== req) return
    list.value = res.data?.list || []
    loaded.value = true
    loadError.value = false
  } catch (err) {
    if (id !== req) return
    loadError.value = true
    loaded.value = false
    throw err
  }
}

function ensure() {
  if (loaded.value) return Promise.resolve()
  if (!inflight) {
    inflight = refresh().finally(() => { inflight = null })
  }
  return inflight
}

async function toggle(menuKey) {
  const id = ++req
  const res = await toggleFavorite({ menuKey })
  if (id !== req) return
  const data = res.data || {}
  if (data.favorited && data.item) {
    list.value = [data.item, ...list.value.filter((x) => x.menuKey !== data.item.menuKey)].slice(0, 12)
  } else {
    list.value = list.value.filter((x) => x.menuKey !== menuKey)
  }
  loaded.value = true
  loadError.value = false
}

export function useFavorites() {
  function isOn(menuKey) {
    return list.value.some((x) => x.menuKey === menuKey)
  }
  return { list, loaded, loadError, ensure, toggle, isOn }
}
