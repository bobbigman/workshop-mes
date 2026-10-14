// Run with Playwright available on NODE_PATH; Vite must be running on COPY_UI_URL.
// API responses are isolated fixtures. This verifies interaction, not live SQL Server.
const { chromium } = require('playwright')
const assert = require('node:assert/strict')
;(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true })
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true })
    await context.addInitScript(() => {
      localStorage.setItem('token', 'ui-test')
      localStorage.setItem('role', '1')
      localStorage.setItem('licenseTier', 'flagship')
    })
    const writes = []
    let failNextProduct = true
    const product = { id: 1, code: 'P1', name: '支架A', unitId: 1, routingId: 1, price: 12, supplier: '供应商A' }
    const route = { id: 1, code: 'R1', name: '支架路线', steps: [{ operationId: 1, seq: 1 }, { operationId: 2, seq: 2 }] }
    const operation = { id: 1, code: 'OP1', name: '下料', deptIds: [1], defectIds: [1] }
    const order = { id: 1, orderNo: 'OLD', productId: 1, productName: '支架A', qty: 100, status: 1, dueDate: '2026-10-20T10:00:00', ext: { 1: '客户A', 999: '非工单字段' }, tasks: [] }
    await context.route(url => url.pathname.startsWith('/api/'), async request => {
      const req = request.request(), path = new URL(req.url()).pathname
      let data = { list: [], total: 0 }
      if (req.method() === 'POST' || req.method() === 'PUT') {
        if (path === '/api/Product' && failNextProduct) {
          failNextProduct = false
          await request.fulfill({ contentType: 'application/json', body: JSON.stringify({ code: 1, msg: '模拟保存失败' }) })
          return
        }
        writes.push({ path, body: req.postDataJSON() })
        await new Promise(resolve => setTimeout(resolve, 250))
      } else if (path === '/api/Product/1') data = product
      else if (path === '/api/Product') data = { list: [product], total: 1 }
      else if (path === '/api/Routing/1') data = route
      else if (path === '/api/Routing') data = { list: [{ ...route, stepNames: '下料 → 检验' }], total: 1 }
      else if (path === '/api/Operation/1') data = operation
      else if (path === '/api/Operation') data = { list: [operation, { id: 2, code: 'OP2', name: '检验' }], total: 2 }
      else if (path === '/api/Department') data = { list: [{ id: 1, name: '加工' }], total: 1 }
      else if (path === '/api/Unit' || path === '/api/DefectItem') data = [{ id: 1, name: path === '/api/Unit' ? '只' : '划伤' }]
      else if (path === '/api/WorkOrder/1') data = order
      else if (path === '/api/WorkOrder') data = { list: [order], total: 1 }
      else if (path === '/api/WorkOrder/status-counts') data = { all: 1, doing: 1 }
      else if (path === '/api/CustomField') data = [{ id: 1, fieldName: '客户', fieldType: 'text' }]
      else if (path === '/api/license/status') data = { effectiveTier: 'flagship', status: 'valid' }
      await request.fulfill({ contentType: 'application/json', body: JSON.stringify({ code: 0, data }) })
    })
    const page = await context.newPage()
    page.on('pageerror', e => console.error('PAGE:', e.message))
    const base = process.env.COPY_UI_URL || 'https://127.0.0.1:5176'
    const dialog = () => page.getByRole('dialog').filter({ visible: true })
    const field = label => dialog().locator('.el-form-item').filter({ has: page.locator('.el-form-item__label', { hasText: label }) }).locator('input').first()
    async function open(entity) {
      await page.goto(`${base}/#/${entity}`)
      await page.getByRole('button', { name: '+ 创建' + ({ product: '产品', routing: '工艺路线', operation: '工序', order: '工单' })[entity], exact: true }).waitFor()
      try { await page.locator('.el-table').getByRole('button', { name: '复制', exact: true }).first().click({ timeout: 15000 }) } catch (e) { console.error('URL:', page.url(), 'BODY:', (await page.locator('body').innerText()).slice(0, 1800)); throw e }
      await dialog().waitFor()
    }
    await open('product')
    assert.equal(await field('产品编号').inputValue(), '')
    assert.equal(await field('产品名称').inputValue(), '支架A')
    await dialog().getByRole('button', { name: '取消', exact: true }).click()
    assert.equal(writes.length, 0)
    await page.locator('.el-table').getByRole('button', { name: '复制', exact: true }).first().click()
    await field('产品编号').fill('P2')
    await field('产品名称').fill('支架B')
    await dialog().getByRole('button', { name: '复制工艺并修改', exact: true }).click()
    await field('新路线编号').fill('R2')
    await dialog().getByRole('button', { name: '↑', exact: true }).nth(1).click()
    await dialog().getByRole('button', { name: '保存', exact: true }).click()
    await page.getByText('模拟保存失败', { exact: true }).waitFor()
    assert.equal(await field('产品编号').inputValue(), 'P2')
    assert.equal(writes.length, 0)
    await dialog().getByRole('button', { name: '保存', exact: true }).click()
    await dialog().waitFor({ state: 'hidden' })
    assert.equal(writes.length, 1)
    assert.equal(writes[0].body.copyFromId, 1)
    assert.equal(writes[0].body.code, 'P2')
    assert.deepEqual(writes[0].body.newRouting.steps.map(x => x.operationId), [2, 1])
    await open('routing')
    await field('路线编号').fill('R3')
    await dialog().getByRole('button', { name: '保存', exact: true }).click()
    await dialog().waitFor({ state: 'hidden' })
    assert.equal(writes[1].body.copyFromId, 1)
    assert.deepEqual(writes[1].body.steps.map(x => x.operationId), [1, 2])
    await open('operation')
    await field('工序编号').fill('OP3')
    await dialog().getByRole('button', { name: '保存', exact: true }).click()
    await dialog().waitFor({ state: 'hidden' })
    assert.deepEqual(writes[2].body.deptIds, [1])
    assert.deepEqual(writes[2].body.defectIds, [1])
    await open('order')
    assert.equal(await field('工单号').inputValue(), '')
    assert.equal(await field('计划交期').inputValue(), '')
    assert.equal(await field('客户').inputValue(), '客户A')
    await field('数量').fill('7')
    await field('数量').blur()
    await dialog().getByRole('button', { name: '保存', exact: true }).click()
    assert.equal(await dialog().getByRole('button', { name: '取消', exact: true }).isEnabled(), false)
    await dialog().waitFor({ state: 'hidden' })
    assert.equal(writes[3].path, '/api/WorkOrder')
    assert.equal(writes[3].body.qty, 7)
    assert.deepEqual(writes[3].body.ext, { 1: '客户A' })
    assert.equal(writes.length, 4)
    console.log('PASS: product cancel/failure retention/composite route, route ordering, operation links, editable order copy, save guard; API mocked')
  } finally { await browser.close() }
})().catch(error => { console.error(error); process.exitCode = 1 })
