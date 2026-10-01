import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { test } from 'node:test'
import assert from 'node:assert/strict'

const source = readFileSync(new URL('./index.vue', import.meta.url), 'utf8')
const handler = source.slice(source.indexOf('async function onLogin()'), source.indexOf('</script>'))

function page(navigate, redirect) {
  const messages = [], paths = [], storage = new Map()
  let loginCount = 0
  const storageApi = {
    getItem: (key) => (storage.has(key) ? storage.get(key) : null),
    setItem: (key, value) => { storage.set(key, String(value)) },
    removeItem: (key) => { storage.delete(key) }
  }
  const context = vm.createContext({
    loading: { value: false },
    clearing: { value: false },
    form: { value: { account: 'test', password: 'test', factoryCode: '' } },
    displayName: { value: '' }, resolveInstanceLabel: value => value,
    login: async () => { loginCount++; return { data: { token: 'test-token', name: 'test', role: 2 } } },
    route: { query: { redirect } },
    router: {
      push: path => { paths.push(path); return navigate() },
      replace: path => { paths.push(path); return navigate() }
    },
    ElMessage: { success: text => messages.push(['success', text]), error: text => messages.push(['error', text]) },
    console: { error() {} }
  })
  context.localStorage = storageApi
  vm.runInContext(handler, context)
  return { context, messages, paths, storage, loginCount: () => loginCount }
}

test('路由完成前不提示成功，并防止重复提交登录', async () => {
  let finish
  const p = page(() => new Promise(resolve => { finish = resolve }))
  const pending = p.context.onLogin()
  await new Promise(resolve => setImmediate(resolve))
  assert.deepEqual(p.messages, [])
  assert.equal(p.context.loading.value, true)
  await p.context.onLogin()
  assert.equal(p.loginCount(), 1)
  finish()
  await pending
  assert.deepEqual(p.paths, ['/h5/home'])
  assert.equal(p.messages[0][0], 'success')
  assert.equal(p.context.loading.value, false)
})

test('模拟手机不支持页面依赖：提示页面加载失败，不误报登录成功', async () => {
  const p = page(async () => { throw new SyntaxError('Unsupported browser syntax') })
  await p.context.onLogin()
  assert.equal(p.messages.length, 1)
  assert.equal(p.messages[0][0], 'error')
  assert.match(p.messages[0][1], /页面加载失败/)
  assert.equal(p.context.loading.value, false)
  assert.equal(p.storage.get('token'), 'test-token')
})

test('路由返回导航失败时也提示失败', async () => {
  const p = page(async () => new Error('Navigation aborted'))
  await p.context.onLogin()
  assert.equal(p.messages[0][0], 'error')
})

test('保留扫码登录后的站内报工回跳', async () => {
  const p = page(async () => {}, '/h5/report?order=GD20260925005')
  await p.context.onLogin()
  assert.deepEqual(p.paths, ['/h5/report?order=GD20260925005'])
  assert.equal(p.messages[0][0], 'success')
})
