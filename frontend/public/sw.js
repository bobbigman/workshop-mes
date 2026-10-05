// 微聚 Service Worker（PWA）
// 目的：满足"添加到主屏幕/桌面图标"的可安装要求，并让已打开的静态资源离线可用。
// 缓存策略刻意保守，避免内网频繁迭代时手机卡旧版：
//   - HTML 导航：network-first，网络失败才回退缓存（保证永远拉到最新 index.html）
//   - 静态资源（/assets/ 带 hash）：cache-first + 后台更新（hash 不变则命中缓存，变了则抓新）
//   - /api/ 与跨域请求：绝不缓存（业务数据必须实时）
const CACHE = 'mes-v2'

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) => cache.add('/')).then(() => self.skipWaiting())
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  )
})

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return

  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api/')) return
  // 上传目录（登录页 banner 等会动态替换的图）：不缓存，实时拉取
  if (url.pathname.startsWith('/uploads/')) return

  // HTML 导航：network-first
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then((res) => {
          const copy = res.clone()
          caches.open(CACHE).then((c) => c.put('/', copy))
          return res
        })
        .catch(() => caches.match('/'))
    )
    return
  }

  // 静态资源：cache-first，未命中时抓取并缓存
  event.respondWith(
    caches.match(req).then((hit) => {
      if (hit) return hit
      return fetch(req).then((res) => {
        if (res.ok) {
          const copy = res.clone()
          caches.open(CACHE).then((c) => c.put(req, copy))
        }
        return res
      })
    })
  )
})
