// 系统自测执行脚本：对任意 REST 资源跑 CRUD 全链路 + 校验，登记每步结果。
// 用法: node crud_selfcheck.mjs <资源名> <字段JSON> [--no-delete]
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'

const BASE = 'http://localhost:8080/api'
const ACCOUNT = process.env.SELFTEST_USER || 'admin'
const PASSWORD = process.env.SELFTEST_PASS || 'Admin123'
const OUT_DIR = join(process.cwd(), '_selfcheck_out')

const results = [] // { module, step, status, info }
function log(module, step, status, info = '') { results.push({ module, step, status, info }) }

async function api(path, { method = 'GET', token, body } = {}) {
  const headers = { 'Content-Type': 'application/json' }
  if (token) headers['Authorization'] = 'Bearer ' + token
  const res = await fetch(BASE + path, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined
  })
  let json = null
  try { json = await res.json() } catch {}
  return { status: res.status, ok: res.ok, json }
}

async function login() {
  const r = await api('/Auth/login', { method: 'POST', body: { account: ACCOUNT, password: PASSWORD } })
  if (!r.ok || !r.json?.data?.token) throw new Error('登录失败: ' + r.status + ' ' + JSON.stringify(r.json))
  return r.json.data
}

async function main() {
  const module = process.argv[2]
  const rawFields = process.argv[3] || process.env.SELFTEST_FIELDS || '{}'
  const fields = JSON.parse(rawFields)
  const noDelete = process.argv.includes('--no-delete')
  const u = await login()
  const token = u.token
  log(module, '登录', '通过', u.name + ' role=' + u.role)

  // 列表
  const lst = await api('/' + module, { token })
  log(module, '查询列表', lst.ok ? '通过' : '失败', 'http=' + lst.status + ' code=' + (lst.json?.code ?? '-') + ' count=' + (lst.json?.data?.length ?? lst.json?.data?.list?.length ?? '-'))

  // 新增（带唯一前缀）
  const ts = Date.now()
  const createBody = JSON.parse(JSON.stringify(fields))
  const matchKey = createBody.code ? 'code' : (createBody.account ? 'account' : (createBody.name ? 'name' : null))
  const matchVal = 'AUTO' + module + '-' + ts
  if (createBody.code) createBody.code = matchVal
  else if (createBody.account) createBody.account = matchVal
  else if (createBody.name) createBody.name = matchVal + '(自测)'
  const cr = await api('/' + module, { method: 'POST', token, body: createBody })
  log(module, '新增', cr.ok ? '通过' : '失败', 'http=' + cr.status + ' code=' + (cr.json?.code ?? '-') + (cr.ok ? '' : ' 返回=' + JSON.stringify(cr.json)))

  if (!cr.ok) { writeOut(module); process.exit(1) }

  // 新增后按唯一字段回查 id（不依赖新增返回结构）
  let id = cr.json?.data?.id ?? (typeof cr.json?.data === 'number' ? cr.json.data : null)
  if (id == null) {
    const lst2 = await api('/' + module + '?page=1&pageSize=500', { token })
    const arr = lst2.json?.data?.list ?? lst2.json?.data
    const found = (Array.isArray(arr) ? arr : []).find(x => (matchKey && String(x[matchKey]) === matchVal) || (matchKey === 'name' && String(x.name) === matchVal + '(自测)'))
    id = found?.id
  }
  log(module, '新增-取回id', id != null ? '通过' : '异常', 'id=' + id + ' key=' + matchKey)
  if (id == null) { writeOut(module); process.exit(1) }

  // 查单个
  const one = await api('/' + module + '/' + id, { token })
  log(module, '查询单个', one.ok ? '通过' : '失败', 'http=' + one.status + (one.json?.data ? ' name=' + (one.json.data.name ?? '') : ''))

  // 编辑（改名称字段）
  const upd = JSON.parse(JSON.stringify(createBody))
  if (upd.name) upd.name = upd.name + '(改)'
  const up = await api('/' + module + '/' + id, { method: 'PUT', token, body: upd })
  log(module, '编辑', up.ok ? '通过' : '失败', 'http=' + up.status + ' code=' + (up.json?.code ?? '-') + (up.ok ? '' : ' 返回=' + JSON.stringify(up.json)))

  // 删除
  if (!noDelete) {
    const del = await api('/' + module + '/' + id, { method: 'DELETE', token })
    log(module, '删除', del.ok ? '通过' : '失败', 'http=' + del.status + ' code=' + (del.json?.code ?? '-') + (del.ok ? '' : ' 返回=' + JSON.stringify(del.json)))
    // 删除后验证：查单个 + 查列表是否残留，区分 硬删/软删/未删
    const chk = await api('/' + module + '/' + id, { token })
    const lstA = await api('/' + module + '?page=1&pageSize=500', { token })
    const arrA = lstA.json?.data?.list ?? lstA.json?.data
    const left = (Array.isArray(arrA) ? arrA : []).filter(x => (matchKey && String(x[matchKey]) === matchVal) || String(x.id) === String(id))
    // 删除后验证：按业务 code 判定（本系统业务错误为 http200+code!=0，勿按 HTTP 状态）
    const gotSingle = chk.ok && chk.json?.code === 0
    const inList = left.length > 0
    let verdict, info
    if (!gotSingle && !inList) { verdict = '通过(已删干净)'; info = '单个 code!=0(不存在) 且 列表无残留' }
    else if (!gotSingle && inList) { verdict = '异常-软删不一致'; info = '单个 code!=0(不存在) 但 列表残留' + left.length }
    else if (gotSingle && !inList) { verdict = '异常-疑似软删'; info = '单个仍可查 但 列表已移除' }
    else { verdict = '异常-删除未生效'; info = '单个可查 且 列表残留' + left.length }
    log(module, '删除后验证', verdict, info)
  }

  writeOut(module)
}

function writeOut(m) {
  mkdirSync(OUT_DIR, { recursive: true })
  const file = join(OUT_DIR, m + '.json')
  writeFileSync(file, JSON.stringify(results, null, 2))
  console.log(JSON.stringify(results, null, 2))
  console.error('已写入: ' + file)
}

main().catch(e => { console.error('自测脚本异常: ' + (e?.message || e)); process.exit(2) })
