import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  FORM_RESTORE_KEY,
  REFRESH_QUERY,
  runClearBrowserCache,
  unregisterMesServiceWorkers,
  deleteMesCaches,
  saveFormForRestore,
  consumeFormRestore,
  buildRefreshUrl,
  stripRefreshParam
} from './clearBrowserCache.js'

function memoryStorage(initial = {}) {
  const map = new Map(Object.entries(initial))
  return {
    getItem: (k) => (map.has(k) ? map.get(k) : null),
    setItem: (k, v) => { map.set(k, String(v)) },
    removeItem: (k) => { map.delete(k) },
    _map: map
  }
}

describe('clearBrowserCache form restore', () => {
  it('saves account/factory only and consumes once', () => {
    const store = memoryStorage()
    saveFormForRestore('alice', 'F01', store)
    const raw = JSON.parse(store.getItem(FORM_RESTORE_KEY))
    assert.equal(raw.account, 'alice')
    assert.equal(raw.factoryCode, 'F01')
    assert.equal('password' in raw, false)
    assert.equal('token' in raw, false)

    const first = consumeFormRestore(store)
    assert.deepEqual(first, { account: 'alice', factoryCode: 'F01' })
    assert.equal(store.getItem(FORM_RESTORE_KEY), null)
    assert.equal(consumeFormRestore(store), null)
  })

  it('rejects expired or invalid restore payload', () => {
    const store = memoryStorage({
      [FORM_RESTORE_KEY]: JSON.stringify({
        account: 'a',
        factoryCode: 'b',
        savedAt: Date.now() - 11 * 60 * 1000
      })
    })
    assert.equal(consumeFormRestore(store), null)

    const bad = memoryStorage({ [FORM_RESTORE_KEY]: '{not-json' })
    assert.equal(consumeFormRestore(bad), null)
  })
})

describe('clearBrowserCache url helpers', () => {
  it('adds refresh query before hash and strips after', () => {
    const href = 'https://mes.local:8080/#/login?redirect=%2Fh5%2Freport'
    const next = buildRefreshUrl(href)
    const u = new URL(next)
    assert.ok(u.searchParams.get(REFRESH_QUERY))
    assert.equal(u.hash, '#/login?redirect=%2Fh5%2Freport')

    let replaced = ''
    const win = {
      location: { href: next },
      history: {
        state: null,
        replaceState(_s, _t, url) { replaced = url }
      }
    }
    // rebuild location-like for strip
    Object.defineProperty(win, 'location', {
      value: {
        href: next,
        get pathname() { return u.pathname },
        get search() { return u.search },
        get hash() { return u.hash }
      }
    })
    // stripRefreshParam uses new URL(win.location.href)
    win.location = { href: next }
    assert.equal(stripRefreshParam(win), true)
    assert.ok(!replaced.includes(REFRESH_QUERY))
    assert.ok(replaced.includes('#/login?redirect=%2Fh5%2Freport'))
  })
})

describe('clearBrowserCache scope', () => {
  it('deletes only mes- caches', async () => {
    const deleted = []
    const win = {
      caches: {
        keys: async () => ['mes-v2', 'other-app', 'mes-old'],
        delete: async (name) => { deleted.push(name); return true }
      }
    }
    const result = await deleteMesCaches(win)
    assert.deepEqual(result.deleted.sort(), ['mes-old', 'mes-v2'])
    assert.deepEqual(deleted.sort(), ['mes-old', 'mes-v2'])
  })

  it('unregisters only root /sw.js', async () => {
    const calls = []
    const win = {
      location: { origin: 'https://mes.local' },
      navigator: {
        serviceWorker: {
          getRegistrations: async () => [
            {
              scope: 'https://mes.local/',
              active: { scriptURL: 'https://mes.local/sw.js' },
              unregister: async () => { calls.push('mes'); return true }
            },
            {
              scope: 'https://mes.local/',
              active: { scriptURL: 'https://mes.local/other-sw.js' },
              unregister: async () => { calls.push('other'); return true }
            },
            {
              scope: 'https://mes.local/sub/',
              active: { scriptURL: 'https://mes.local/sw.js' },
              unregister: async () => { calls.push('sub'); return true }
            }
          ]
        }
      }
    }
    const result = await unregisterMesServiceWorkers(win)
    assert.equal(result.unregistered, 1)
    assert.deepEqual(calls, ['mes'])
  })
})

describe('runClearBrowserCache', () => {
  it('keeps login storage and fails without auto-success on api error', async () => {
    const local = memoryStorage({ token: 'keep-me', userName: 'bob' })
    const win = {
      isSecureContext: true,
      localStorage: local,
      navigator: {},
      caches: undefined
    }
    const result = await runClearBrowserCache({
      win,
      requestClear: async () => {
        const e = new Error('接口返回非 JSON')
        e.status = 200
        e.url = '/api/Auth/clear-browser-cache'
        throw e
      }
    })
    assert.equal(result.ok, false)
    assert.equal(result.step, 'api')
    assert.match(result.message, /清缓存接口失败/)
    assert.equal(local.getItem('token'), 'keep-me')
    assert.equal(local.getItem('userName'), 'bob')
  })

  it('reports limited capability on insecure context after successful api', async () => {
    const win = {
      isSecureContext: false,
      navigator: {},
      caches: undefined
    }
    const result = await runClearBrowserCache({
      win,
      requestClear: async () => ({ code: 0, data: { httpCacheClearRequested: true } })
    })
    assert.equal(result.ok, true)
    assert.equal(result.limited, true)
    assert.match(result.message, /无法完整自动清理缓存/)
    assert.match(result.message, /登录信息保留/)
  })

  it('succeeds when api + secure context + no sw/cache apis beyond missing', async () => {
    const win = {
      isSecureContext: true,
      location: { origin: 'https://mes.local' },
      navigator: {
        serviceWorker: {
          getRegistrations: async () => []
        }
      },
      caches: {
        keys: async () => [],
        delete: async () => true
      }
    }
    const result = await runClearBrowserCache({
      win,
      requestClear: async () => ({ code: 0 })
    })
    assert.equal(result.ok, true)
    assert.equal(result.limited, false)
    assert.match(result.message, /缓存清理请求已完成/)
  })

  it('surfaces cache delete failures with step context', async () => {
    const win = {
      isSecureContext: true,
      location: { origin: 'https://mes.local' },
      navigator: {
        serviceWorker: { getRegistrations: async () => [] }
      },
      caches: {
        keys: async () => ['mes-v2'],
        delete: async () => { throw new Error('QuotaExceeded') }
      }
    }
    const result = await runClearBrowserCache({
      win,
      requestClear: async () => ({ code: 0 })
    })
    assert.equal(result.ok, false)
    assert.equal(result.step, 'cache-storage')
    assert.match(result.message, /清理本地资源缓存失败/)
  })
})
