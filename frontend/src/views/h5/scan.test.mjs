import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { ref, computed } from 'vue'
import { parseOrderNo, parseScanPayload } from '../../utils/scanPayload.js'

// 执行真实页面脚本，仅替换浏览器设备、路由和 jsQR 解码器，覆盖异步授权竞态。
const source = readFileSync(new URL('./scan.vue', import.meta.url), 'utf8')
  .match(/<script setup>([\s\S]*?)<\/script>/)[1]
  .replace(/^import .*$/gm, '')
function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function mediaStream() {
  const track = { stopped: false, stop() { this.stopped = true } }
  return { track, getTracks: () => [track] }
}
function page({ secure = true, getUserMedia, play = async () => {}, diagnostics = false, storage = new Map() } = {}) {
  const timers = new Map(), intervals = new Map(), decodeQueue = [], routes = [], logs = []
  let sequence = 0, unmount, mount
  const context = vm.createContext({
    ref, computed, nextTick: async () => {}, onMounted: fn => { mount = fn },
    onBeforeUnmount: fn => { unmount = fn },
    useRouter: () => ({ push: route => routes.push(route) }),
    showToast: () => {}, parseOrderNo, parseScanPayload,
    URLSearchParams,
    // 纯 JS 解码器：返回队首结果，无码返回 null。
    jsQR: () => (decodeQueue.length ? decodeQueue.shift() : null),
    window: {
      isSecureContext: secure, location: { search: diagnostics ? '?scan-check=0922-4' : '', href: 'https://x/#/h5/scan' },
      localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
      addEventListener() {}, removeEventListener() {},
      performance: { getEntriesByType: () => [{ type: 'reload' }] }
    },
    navigator: { mediaDevices: { getUserMedia } },
    document: {
      hidden: false, visibilityState: 'visible', addEventListener() {}, removeEventListener() {},
      createElement: tag => tag === 'canvas'
        ? { width: 0, height: 0, getContext: () => ({ drawImage() {}, getImageData: () => ({ data: [] }) }) }
        : { value: '', select() {} },
      body: { appendChild() {}, removeChild() {} }
    },
    localStorage: { getItem() {}, setItem() {} },
    console: { error: (...args) => logs.push(args) },
    setTimeout: fn => { timers.set(++sequence, fn); return sequence },
    clearTimeout: id => timers.delete(id),
    setInterval: fn => { intervals.set(++sequence, fn); return sequence },
    clearInterval: id => intervals.delete(id)
  })
  vm.runInContext(source + '\n globalThis.page = { startCamera, cancelStart, stopCamera, openManual, switchToPda, onVisibility, onPageHide, videoEl, cameraState, cameraHint, starting, diagnosticLines }', context)
  const api = context.page
  if (!api.videoEl) throw new Error('scan.vue 测试夹具未导出 videoEl')
  api.videoEl.value = { srcObject: null, play: play || (async () => {}), videoWidth: 640, videoHeight: 480 }
  // 触发一次识别循环：入队解码结果并执行一帧采样。
  const decode = result => { decodeQueue.push(result); const cb = [...intervals.values()][0]; if (cb) cb() }
  return { ...api, decode, routes, logs, timers, mount: () => mount(), unmount: () => unmount(), context }
}

test('点击同步请求权限，显示启动状态；成功扫码停流并进入报工页', async () => {
  const grant = deferred(), stream = mediaStream()
  let requested = false
  const p = page({ getUserMedia: options => {
    requested = true
    assert.equal(options.audio, false)
    assert.ok(options.video)
    return grant.promise
  } })
  const start = p.startCamera()
  assert.equal(requested, true)
  assert.equal(p.cameraState.value, 'starting')
  grant.resolve(stream)
  await start
  assert.equal(p.cameraState.value, 'scanning')
  p.decode({ data: 'GD001' })
  assert.equal(stream.track.stopped, true)
  assert.equal(p.routes[0].query.order, 'GD001')
})

test('HTTP 与不支持摄像头时直接显示原因', async () => {
  const p = page({ secure: false })
  await p.startCamera()
  assert.match(p.cameraHint.value, /HTTPS/)
  const unsupported = page()
  await unsupported.startCamera()
  assert.match(unsupported.cameraHint.value, /不支持摄像头/)
})

for (const [name, hint] of [['NotAllowedError', /允许摄像头/], ['NotFoundError', /未检测到/], ['NotReadableError', /被占用/]]) {
  test(`保留设备错误 ${name} 并允许重试`, async () => {
    const p = page({ getUserMedia: async () => { throw Object.assign(new Error(name), { name }) } })
    await p.startCamera()
    assert.equal(p.cameraState.value, 'error')
    assert.equal(p.starting.value, false)
    assert.match(p.cameraHint.value, hint)
    assert.equal(p.logs.length, 1)
  })
}

test('授权无响应时超时提示；迟到授权立即释放', async () => {
  const grant = deferred(), stream = mediaStream()
  const p = page({ getUserMedia: () => grant.promise })
  const start = p.startCamera()
  ;[...p.timers.values()][0]()
  assert.equal(p.starting.value, false)
  assert.match(p.cameraHint.value, /超时/)
  grant.resolve(stream)
  await start
  assert.equal(stream.track.stopped, true)
  assert.equal(p.cameraState.value, 'error')
})

test('取消后重试，旧授权不关闭新摄像头', async () => {
  const old = deferred(), oldStream = mediaStream(), current = mediaStream()
  let calls = 0
  const p = page({ getUserMedia: () => ++calls === 1 ? old.promise : Promise.resolve(current) })
  const first = p.startCamera()
  p.cancelStart()
  await p.startCamera()
  old.resolve(oldStream)
  await first
  assert.equal(oldStream.track.stopped, true)
  assert.equal(current.track.stopped, false)
  assert.equal(p.cameraState.value, 'scanning')
})

test('旧播放失败不破坏新一次重试', async () => {
  const oldPlay = deferred(), playing = deferred(), firstStream = mediaStream(), secondStream = mediaStream()
  let calls = 0, plays = 0
  const p = page({ getUserMedia: async () => ++calls === 1 ? firstStream : secondStream,
    play: () => {
      if (++plays !== 1) return Promise.resolve()
      playing.resolve()
      return oldPlay.promise
    } })
  const first = p.startCamera()
  await playing.promise
  p.cancelStart()
  await p.startCamera()
  oldPlay.reject(new Error('old playback failed'))
  await first
  assert.equal(secondStream.track.stopped, false)
  assert.equal(p.cameraState.value, 'scanning')
})

for (const action of ['openManual', 'switchToPda', 'unmount']) {
  test(`${action} 后迟到的授权不再启动摄像头`, async () => {
    const grant = deferred(), stream = mediaStream()
    const p = page({ getUserMedia: () => grant.promise })
    const start = p.startCamera()
    p[action]()
    grant.resolve(stream)
    await start
    assert.equal(stream.track.stopped, true)
    assert.notEqual(p.cameraState.value, 'scanning')
  })
}

test('无二维码帧继续识别，不中断循环', async () => {
  const stream = mediaStream(), p = page({ getUserMedia: async () => stream })
  await p.startCamera()
  assert.equal(p.cameraState.value, 'scanning')
  p.decode(null)
  assert.equal(p.cameraState.value, 'scanning')
  assert.equal(stream.track.stopped, false)
})

test('授权窗口使页面短暂隐藏后返回，仍能完成启动', async () => {
  const grant = deferred(), stream = mediaStream()
  const p = page({ getUserMedia: () => grant.promise })
  const start = p.startCamera()
  p.context.document.hidden = true
  p.onVisibility()
  assert.equal(p.cameraState.value, 'starting')
  p.context.document.hidden = false
  p.onVisibility()
  grant.resolve(stream)
  await start
  assert.equal(p.cameraState.value, 'scanning')
  assert.equal(stream.track.stopped, false)
})

test('授权返回时仍在后台，释放视频并保留原因', async () => {
  const grant = deferred(), stream = mediaStream()
  const p = page({ getUserMedia: () => grant.promise })
  const start = p.startCamera()
  p.context.document.hidden = true
  p.onVisibility()
  grant.resolve(stream)
  await start
  assert.equal(stream.track.stopped, true)
  assert.equal(p.cameraState.value, 'error')
  assert.match(p.cameraHint.value, /页面不在前台/)
  p.context.document.hidden = false
  p.onVisibility()
  assert.match(p.cameraHint.value, /页面不在前台/)
})

test('扫描期间进入后台立即释放摄像头，返回后不自动启动', async () => {
  const stream = mediaStream(), p = page({ getUserMedia: async () => stream })
  await p.startCamera()
  p.context.document.hidden = true
  p.onVisibility()
  assert.equal(stream.track.stopped, true)
  assert.match(p.cameraHint.value, /已暂停/)
  p.context.document.hidden = false
  p.onVisibility()
  assert.equal(p.cameraState.value, 'idle')
})

test('检查链接保留权限失败步骤及错误名，普通页面不生成诊断记录', async () => {
  const getUserMedia = async () => { throw Object.assign(new Error('private error detail'), { name: 'NotAllowedError' }) }
  const checked = page({ diagnostics: true, getUserMedia })
  await checked.startCamera()
  const lines = checked.diagnosticLines.value.join('\n')
  assert.match(lines, /开始向浏览器请求摄像头权限/)
  assert.match(lines, /NotAllowedError/)
  assert.doesNotMatch(lines, /private error detail/)
  const normal = page({ getUserMedia })
  await normal.startCamera()
  assert.equal(normal.diagnosticLines.value.length, 0)
})

test('授权界面触发的 pagehide 不取消进行中的权限请求', async () => {
  const grant = deferred(), stream = mediaStream()
  const p = page({ diagnostics: true, getUserMedia: () => grant.promise })
  const start = p.startCamera()
  p.onPageHide({ persisted: false })
  grant.resolve(stream)
  await start
  assert.equal(p.cameraState.value, 'scanning')
  assert.match(p.diagnosticLines.value.join('\n'), /浏览器离开页面/)
})

test('请求摄像头期间页面重载，保留上一页记录并显示重载说明', async () => {
  const storage = new Map()
  storage.set('h5.scan.diagnostics.local', JSON.stringify({
    lines: ['08:00:00 开始向浏览器请求摄像头权限（默认摄像头）'],
    pending: true
  }))
  const after = page({ diagnostics: true, storage })
  after.mount()
  const lines = after.diagnosticLines.value.join('\n')
  assert.match(lines, /开始向浏览器请求摄像头权限/)
  assert.match(lines, /上次摄像头请求未完成/)
  assert.match(after.cameraHint.value, /手机自带浏览器/)
})

test('正常启动后刷新，不误报上次请求未完成', async () => {
  const storage = new Map(), stream = mediaStream()
  const before = page({ diagnostics: true, storage, getUserMedia: async () => stream })
  await before.startCamera()
  before.onPageHide({ persisted: true })
  assert.equal(stream.track.stopped, true)
  const after = page({ diagnostics: true, storage })
  after.mount()
  assert.doesNotMatch(after.diagnosticLines.value.join('\n'), /上次摄像头请求未完成/)
})
