import test from 'node:test'
import assert from 'node:assert/strict'
import { createAiIdentityBoundary } from './aiIdentityBoundary.js'

test('身份变化清理一次，旧结果与旧finally不可写入新身份', () => {
  let identity = 'A:user1', clears = 0
  const gate = createAiIdentityBoundary(() => identity, () => clears++)
  const old = gate.begin()
  identity = 'B:user2'
  assert.equal(gate.current(old), false)
  assert.equal(clears, 1)
  const fresh = gate.begin()
  assert.equal(gate.current(old), false)
  assert.equal(gate.current(fresh), true)
  assert.equal(clears, 1)
})
test('新对话、后发请求及卸载使旧结果失效', () => {
  const gate = createAiIdentityBoundary(() => 'A:user1', () => {})
  const old = gate.begin(), next = gate.begin()
  assert.equal(gate.current(old), false)
  assert.equal(gate.current(next), true)
  gate.invalidate()
  assert.equal(gate.current(next), false)
  const last = gate.begin()
  gate.dispose()
  assert.equal(gate.current(last), false)
})
test('延迟响应在切厂后不进入新会话', async () => {
  let identity = 'A', apply = 0, release
  const gate = createAiIdentityBoundary(() => identity, () => {})
  const ticket = gate.begin()
  const response = new Promise(resolve => { release = resolve }).then(() => { if (gate.current(ticket)) apply++ })
  identity = 'B'; gate.check(); gate.begin(); release()
  await response
  assert.equal(apply, 0)
})
