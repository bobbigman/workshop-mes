import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loginDestination } from './loginDestination.js'

const pc = { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)', maxTouchPoints: 0 }
const phone = { userAgent: 'Mozilla/5.0 (Linux; Android 12; PDA)', maxTouchPoints: 1 }
test('手机各角色默认工作台，电脑保留角色默认入口', () => {
  for (const role of [1, 2, 3]) assert.equal(loginDestination(null, role, phone), '/h5/home')
  for (const role of [1, 3]) assert.equal(loginDestination(null, role, pc), '/home')
  assert.equal(loginDestination(null, 2, pc), '/h5/home')
  assert.equal(loginDestination(null, 1, { userAgent: 'Macintosh', maxTouchPoints: 5 }), '/h5/home')
})
test('直达参数优先，外站及异常路径不作为回跳', () => {
  const target = '/h5/report?order=A001&task=7'
  assert.equal(loginDestination(target, 1, phone), target)
  assert.equal(loginDestination('/h5/report-authorize?code=x', 2, pc), '/h5/report-authorize?code=x')
  for (const target of ['https://evil.test', '//evil.test', '/report', '/h5/\\evil', '/h5/report\n']) {
    assert.equal(loginDestination(target, 1, pc), '/home')
  }
})
