import { test } from 'node:test'
import assert from 'node:assert/strict'
import { canOpenReportManagement, isReportManagementEntry, managementDestination, updateManagementTime, managementTimeError } from './reportManagement.js'

test('工人/班组长/管理员的管理action边界及伪造参数', () => {
  for (const role of [0, 2, 9, null]) for (const action of ['proxy', 'batch', 'pending']) assert.equal(canOpenReportManagement(action, role), false)
  assert.equal(canOpenReportManagement('proxy', '3'), true)
  for (const action of ['batch', 'pending']) assert.equal(canOpenReportManagement(action, 3), false)
  for (const action of ['proxy', 'batch', 'pending']) assert.equal(canOpenReportManagement(action, 1), true)
  for (const action of ['', 'delete', ['proxy'], '__proto__']) assert.equal(canOpenReportManagement(action, 1), false)
})
test('普通PC/旧工单定位不被识别为工作台管理直达', () => {
  for (const query of [{}, { order: 'A001' }, { action: 'proxy' }, { from: 'h5' }]) assert.equal(isReportManagementEntry(query), false)
  const route = managementDestination('proxy')
  assert.equal(isReportManagementEntry(route.query), true)
  assert.equal(route.path, '/report')
})
test('手机分行起止时间保留秒，清空/缺一端/反序/超24小时', () => {
  let range = updateManagementTime(null, 0, '2026-10-22T08:00')
  assert.equal(range[0], '2026-10-22T08:00:00')
  assert.ok(managementTimeError(range))
  range = updateManagementTime(range, 1, '2026-10-22T09:00:07')
  assert.equal(managementTimeError(range), '')
  assert.equal(range[1], '2026-10-22T09:00:07')
  assert.ok(managementTimeError([range[1], range[0]]))
  assert.ok(managementTimeError([range[0], '2026-10-24T08:00:00']))
  assert.equal(updateManagementTime(updateManagementTime(range, 0, ''), 1, ''), null)
})
