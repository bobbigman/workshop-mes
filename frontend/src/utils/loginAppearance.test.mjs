import { test } from 'node:test'
import assert from 'node:assert/strict'
import { LOGIN_FACTORY_KEY, resolveLoginFactory, rememberLoginFactory } from './loginAppearance.js'

function storage(values = {}) {
  const data = new Map(Object.entries(values))
  return {
    getItem: key => data.get(key) ?? null,
    setItem: (key, value) => data.set(key, value)
  }
}

test('首次打开采用实例工厂，未知实例使用公共背景', () => {
  assert.equal(resolveLoginFactory(undefined, 'F001', storage()), 'F001')
  assert.equal(resolveLoginFactory(undefined, '', storage()), '')
})

test('记住已登录工厂，重新打开时不被实例默认厂覆盖', () => {
  const local = storage()
  rememberLoginFactory('SUN', local)
  assert.equal(resolveLoginFactory(undefined, 'F001', local), 'SUN')
  assert.equal(local.getItem(LOGIN_FACTORY_KEY), 'SUN')
  assert.equal(local.getItem('token'), null)
})

test('兼容更新前已有的登录工厂记录，明确链接工厂优先', () => {
  const local = storage({ factoryCode: 'SUN' })
  assert.equal(resolveLoginFactory(undefined, 'F001', local), 'SUN')
  assert.equal(resolveLoginFactory(' OTHER ', 'F001', local), 'OTHER')
  assert.equal(resolveLoginFactory(['OTHER'], 'F001', local), 'SUN')
})
