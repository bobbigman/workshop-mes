import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { parseVoiceReport, spokenInteger } from './voiceReport.js'
const options = [{id: 21, label:'包装'}, {id: 22,label:'装配'}]
describe('语音填报安全解析', () => {
  it('中文数量和数字整数', () => {
    for (const [text, qty] of [['五十',50],['两百',200],['一百零二',102],['一千二百三十四',1234],['一万零三',10003],['零五',5],['50',50]]) assert.equal(spokenInteger(text), qty)
  })
  it('任务序号来自当前列表', () => {
    assert.equal(parseVoiceReport('第一项五十件',options).option.id,21)
    assert.equal(parseVoiceReport('第2项，良品60件。',options).goodQty,60)
    assert.throws(()=>parseVoiceReport('第三项五十件',options))
  })
  it('工序名称或当前已选工序', () => {
    assert.equal(parseVoiceReport('包装五十件',options).option.id,21)
    assert.equal(parseVoiceReport('五十件',options,22).option.id,22)
    assert.throws(()=>parseVoiceReport('五十件',options))
  })
  it('明确改成只覆盖数量', () => {
    const result=parseVoiceReport('第一项五十件，不对，改成六十件',options)
    assert.equal(result.option.id,21); assert.equal(result.goodQty,60)
    assert.throws(()=>parseVoiceReport('包装五十件改成装配六十件',options))
  })
  it('不把多个数字或复杂报工误当单项良品', () => {
    for (const text of ['包装50件不良2件','包装50件装配60件','包装-50件','包装1.5件','包装1。5件','包装零件','包装一百二件','包装十百件','包装50件改成60件改成70件','包装昨天50件今天60件','包装50件确认提交','包装999999999999件']) assert.throws(()=>parseVoiceReport(text,options),text)
  })
  it('重名工序必须序号选择', () => {
    assert.throws(()=>parseVoiceReport('包装50件',[...options,{id:23,label:'包装'}]))
    assert.equal(parseVoiceReport('第三项50件',[...options,{id:23,label:'包装'}]).option.id,23)
  })
  it('空列表和已失效选择拒绝', () => {
    assert.throws(()=>parseVoiceReport('第一项50件',[]))
    assert.throws(()=>parseVoiceReport('50件',options,999))
  })
})
