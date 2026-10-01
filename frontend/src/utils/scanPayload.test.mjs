import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { parseOrderNo, parseScanPayload, ORDER_NO_MAX_LEN } from './scanPayload.js'

const ORIGIN = 'https://mes.local:8080'

describe('parseOrderNo', () => {
  it('accepts long / short / custom codes', () => {
    assert.deepEqual(parseOrderNo('GD20260914004'), { ok: true, orderNo: 'GD20260914004' })
    assert.deepEqual(parseOrderNo(' A0001 '), { ok: true, orderNo: 'A0001' })
    assert.deepEqual(parseOrderNo('wo-自定义_01'), { ok: true, orderNo: 'wo-自定义_01' })
  })

  it('rejects blank and overlong', () => {
    assert.equal(parseOrderNo('').ok, false)
    assert.equal(parseOrderNo('   ').ok, false)
    assert.equal(parseOrderNo('x'.repeat(ORDER_NO_MAX_LEN + 1)).ok, false)
    assert.equal(parseOrderNo('x'.repeat(ORDER_NO_MAX_LEN)).ok, true)
  })

  it('does not force case change', () => {
    assert.deepEqual(parseOrderNo('AbC'), { ok: true, orderNo: 'AbC' })
  })
})

describe('parseScanPayload', () => {
  it('parses plain order numbers', () => {
    assert.deepEqual(parseScanPayload('GD20260914004'), { ok: true, orderNo: 'GD20260914004' })
  })

  it('parses same-origin report links', () => {
    const url = `${ORIGIN}/#/h5/report?order=GD20260914004`
    assert.deepEqual(parseScanPayload(url, { origin: ORIGIN }), { ok: true, orderNo: 'GD20260914004' })
  })

  it('parses report links with op', () => {
    const url = `${ORIGIN}/#/h5/report?order=GD20260914004&op=12`
    assert.deepEqual(parseScanPayload(url, { origin: ORIGIN }), {
      ok: true,
      orderNo: 'GD20260914004',
      operationId: 12
    })
  })

  it('ignores invalid or duplicate op', () => {
    assert.deepEqual(
      parseScanPayload(`${ORIGIN}/#/h5/report?order=GD1&op=abc`, { origin: ORIGIN }),
      { ok: true, orderNo: 'GD1' }
    )
    assert.deepEqual(
      parseScanPayload(`${ORIGIN}/#/h5/report?order=GD1&op=0`, { origin: ORIGIN }),
      { ok: true, orderNo: 'GD1' }
    )
    assert.deepEqual(
      parseScanPayload(`${ORIGIN}/#/h5/report?order=GD1&op=1&op=2`, { origin: ORIGIN }),
      { ok: true, orderNo: 'GD1' }
    )
  })

  it('decodes encoded order once', () => {
    const url = `${ORIGIN}/#/h5/report?order=${encodeURIComponent('工单-01')}`
    assert.deepEqual(parseScanPayload(url, { origin: ORIGIN }), { ok: true, orderNo: '工单-01' })
  })

  it('rejects missing / duplicate order', () => {
    assert.equal(parseScanPayload(`${ORIGIN}/#/h5/report`, { origin: ORIGIN }).ok, false)
    assert.equal(
      parseScanPayload(`${ORIGIN}/#/h5/report?order=a&order=b`, { origin: ORIGIN }).ok,
      false
    )
  })

  it('rejects wrong route / foreign origin / bad protocol', () => {
    assert.equal(parseScanPayload(`${ORIGIN}/#/h5/scan?order=a`, { origin: ORIGIN }).ok, false)
    assert.equal(parseScanPayload(`${ORIGIN}/#/report?order=a`, { origin: ORIGIN }).ok, false)
    assert.equal(
      parseScanPayload('https://evil.example/#/h5/report?order=a', { origin: ORIGIN }).ok,
      false
    )
    assert.equal(
      parseScanPayload('http://mes.local:8080/#/h5/report?order=a', { origin: ORIGIN }).ok,
      false
    )
    assert.equal(parseScanPayload('ftp://mes.local/#/h5/report?order=a', { origin: ORIGIN }).ok, false)
    assert.equal(
      parseScanPayload('https://user:pass@mes.local:8080/#/h5/report?order=a', { origin: ORIGIN }).ok,
      false
    )
  })
})
