const DIGITS = { 零: 0, 〇: 0, 一: 1, 二: 2, 两: 2, 三: 3, 四: 4, 五: 5, 六: 6, 七: 7, 八: 8, 九: 9 }
const NUMBER = '[0-9零〇一二两三四五六七八九十百千万]+'

export function spokenInteger(value) {
  if (/^\d+$/.test(value)) {
    const n = Number(value)
    if (Number.isSafeInteger(n) && n <= 2147483647) return n
    throw new Error('数量过大，请核对')
  }
  if (!/^[零〇一二两三四五六七八九十百千万]+$/.test(value)) throw new Error('请说整数件数')
  if (!/[十百千万]/.test(value)) return spokenInteger([...value].map(c => DIGITS[c]).join(''))
  if (/[百千万][一二两三四五六七八九]$/.test(value)) throw new Error('请说完整数量，例如一百二十或一百零二')
  let total = 0, section = 0, digit = 0, previousUnit = 10000, hasDigit = false, hasWan = false
  for (const c of value) {
    if (c in DIGITS) {
      if (hasDigit && digit !== 0) throw new Error('中文数量不明确，请核对')
      digit = DIGITS[c]; hasDigit = true; continue
    }
    if (c === '万') {
      if (hasWan || section + digit === 0) throw new Error('中文数量不明确，请核对')
      total = (section + digit) * 10000; section = 0; digit = 0; previousUnit = 10000; hasDigit = false; hasWan = true; continue
    }
    const unit = { 十: 10, 百: 100, 千: 1000 }[c]
    if (unit >= previousUnit || (!hasDigit && !(unit === 10 && section === 0))) throw new Error('中文数量不明确，请核对')
    section += (hasDigit ? digit : 1) * unit; digit = 0; hasDigit = false; previousUnit = unit
  }
  return spokenInteger(String(total + section + digit))
}

// 仅识别明确的单项良品指令，业务权限与可报数量仍由原接口校验。
export function parseVoiceReport(text, options, selectedId = null) {
  if (/[0-9][。．.][0-9]/.test(String(text))) throw new Error('本次只支持整数件数')
  let source = String(text || '').trim().replace(/[，,。！!？?、\s]/g, '')
  if (!source || source.length > 120) throw new Error('请说工序和良品件数，例如“包装五十件”')
  const corrections = source.split(/(?:不对)?改[成为]/)
  if (corrections.length > 2) throw new Error('多次修改请直接说最终工序和数量')
  if (corrections.length === 2) {
    const original = parseVoiceReport(corrections[0], options, selectedId)
    const final = corrections[1].match(new RegExp(`^(?:良品)?(${NUMBER})(?:件|个)?$`))
    if (!final) throw new Error('修改后请只说最终良品件数')
    const goodQty = spokenInteger(final[1])
    if (goodQty <= 0) throw new Error('良品数量必须大于零')
    return { ...original, goodQty }
  }
  let option, quantity
  const ordinal = source.match(new RegExp(`^第(${NUMBER})(?:项|个)(.*)$`))
  if (ordinal) {
    option = options[spokenInteger(ordinal[1]) - 1]
    if (!option) throw new Error('序号不在当前列表中，请核对')
    quantity = ordinal[2]
  } else {
    const matched = options.filter(o => source.startsWith(o.label))
    if (matched.length > 1) throw new Error('工序名称重复或相近，请说列表序号')
    if (matched.length === 1) { option = matched[0]; quantity = source.slice(option.label.length) }
    else { option = options.find(o => String(o.id) === String(selectedId)); quantity = source }
  }
  if (!option) throw new Error('请先选择工序，或说“第一项五十件”')
  const match = quantity.match(new RegExp(`^(?:本次)?(?:完成|良品|报工|报)?(${NUMBER})(?:件|个)?$`))
  if (!match) throw new Error('本次只支持一项良品整数，例如“包装五十件”；不良或多人请用表单')
  const goodQty = spokenInteger(match[1])
  if (goodQty <= 0) throw new Error('良品数量必须大于零')
  return { option, goodQty }
}
