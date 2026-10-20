import { test } from 'node:test'
import assert from 'node:assert/strict'
import { hasAnswerTable, formatAiBusinessDate } from './aiWorkOrderPresentation.js'

test('原始工单表格仅在回答确有表格时默认收起', () => {
  assert.equal(hasAnswerTable('共7条，请看下面的数据。'), false)
  assert.equal(hasAnswerTable('|单号|数量|\n|-|-|\n|A0001|10|'), true)
  assert.equal(hasAnswerTable('```\n|单号|数量|\n|-|-|\n```'), false)
  assert.equal(hasAnswerTable(''), false)
})

test('交期保持返回的业务日期，不提前一天', () => {
  assert.equal(formatAiBusinessDate('2026-10-09T00:00:00+08:00'), '2026-10-09')
  assert.equal(formatAiBusinessDate('2026-10-09T00:00:00'), '2026-10-09')
  assert.equal(formatAiBusinessDate(null), '—')
})
