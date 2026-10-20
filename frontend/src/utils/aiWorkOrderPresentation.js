import { parseAiMarkdown } from './aiMarkdown.js'

export function hasAnswerTable(text) {
  return parseAiMarkdown(text).some(block => block.type === 'table')
}

// 交期是业务日期；不能把 +08:00 的午夜转 UTC，导致显示成前一天。
export function formatAiBusinessDate(value) {
  if (!value) return '—'
  return String(value).slice(0, 10)
}
