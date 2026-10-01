// 批量 CRUD 自测 v2：登录重试 + 业务 code=0 才算通过 + Routing 自动带 steps。用法: node run_batch.mjs
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'
const BASE='http://localhost:8080/api'
const OUT_DIR=join(process.cwd(),'_selfcheck_out')
const results=[]
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
function log(m,step,status,info=''){results.push({module:m,step,status,info})}
async function api(p,{method='GET',token,body}={}){
  const headers={'Content-Type':'application/json'}; if(token)headers['Authorization']='Bearer '+token
  const r=await fetch(BASE+p,{method,headers,body:body?JSON.stringify(body):undefined})
  let j=null; try{j=await r.json()}catch{}
  return{status:r.status,ok:r.ok,json:j}
}
async function login(retry=3){
  for(let i=0;i<retry;i++){
    try{const r=await api('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}})
      if(r.ok&&r.json?.data?.token)return r.json.data}catch{}
    await sleep(600)
  }
  throw new Error('登录失败(重试'+retry+'次)')
}
async function run(module,fields,noDelete){
  const u=await login()
  const token=u.token
  log(module,'登录','通过',u.name+' role='+u.role)
  const lst=await api('/'+module,{token})
  log(module,'查询列表',(lst.ok&&lst.json?.code===0)?'通过':'失败','http='+lst.status+' code='+(lst.json?.code??'-')+' count='+(lst.json?.data?.length??lst.json?.data?.list?.length??'-'))
  // Routing 需要 steps：取一个现有工序 id
  let extra={}
  if(module==='Routing'){
    const ops=await api('/Operation?page=1&pageSize=100',{token})
    const arr=ops.json?.data?.list??ops.json?.data??[]
    const op=(Array.isArray(arr)?arr:[]).find(o=>o.id)
    if(op){extra.steps=[{operationId:op.id,seq:1}]; log(module,'前置-取工序','通过','opId='+op.id)}
    else{log(module,'前置-取工序','异常','无可用工序');return}
  }
  const ts=Date.now()
  const cb=Object.assign(JSON.parse(JSON.stringify(fields)),extra)
  const mk=cb.code?'code':(cb.account?'account':(cb.name?'name':null))
  const mv='AUTO'+module+'-'+ts
  if(cb.code)cb.code=mv; else if(cb.account)cb.account=mv; else if(cb.name)cb.name=mv+'(自测)'
  const cr=await api('/'+module,{method:'POST',token,body:cb})
  const crOk=cr.ok&&cr.json?.code===0
  log(module,'新增',crOk?'通过':'失败','http='+cr.status+' code='+(cr.json?.code??'-')+(crOk?'':' 返回='+JSON.stringify(cr.json)))
  if(!crOk)return
  let id=cr.json?.data?.id??(typeof cr.json?.data==='number'?cr.json.data:null)
  if(id==null){
    const l2=await api('/'+module+'?page=1&pageSize=500',{token})
    const arr=l2.json?.data?.list??l2.json?.data
    id=(Array.isArray(arr)?arr:[]).find(x=>(mk&&String(x[mk])===mv)||(mk==='name'&&String(x.name)===mv+'(自测)'))?.id
  }
  log(module,'新增-取回id',id!=null?'通过':'异常','id='+id+' key='+mk)
  if(id==null)return
  const one=await api('/'+module+'/'+id,{token})
  log(module,'查询单个',(one.ok&&one.json?.code===0)?'通过':'失败','http='+one.status+(one.json?.data?' name='+(one.json.data.name??''):''))
  const upd=JSON.parse(JSON.stringify(cb)); if(upd.name)upd.name=upd.name+'(改)'
  const up=await api('/'+module+'/'+id,{method:'PUT',token,body:upd})
  const upOk=up.ok&&up.json?.code===0
  log(module,'编辑',upOk?'通过':'失败','http='+up.status+' code='+(up.json?.code??'-')+(upOk?'':' 返回='+JSON.stringify(up.json)))
  if(!noDelete){
    const del=await api('/'+module+'/'+id,{method:'DELETE',token})
    const delOk=del.ok&&del.json?.code===0
    log(module,'删除',delOk?'通过':'失败','http='+del.status+' code='+(del.json?.code??'-')+(delOk?'':' 返回='+JSON.stringify(del.json)))
    const chk=await api('/'+module+'/'+id,{token})
    const la=await api('/'+module+'?page=1&pageSize=500',{token})
    const arrA=la.json?.data?.list??la.json?.data
    const left=(Array.isArray(arrA)?arrA:[]).filter(x=>(mk&&String(x[mk])===mv)||String(x.id)===String(id))
    const gs=chk.ok&&chk.status===200, il=left.length>0
    let v,inf
    if(!gs&&!il){v='通过(已删干净)';inf='单个404 且 列表无残留'}
    else if(!gs&&il){v='异常-软删不一致';inf='单个404 但 列表残留'+left.length}
    else if(gs&&!il){v='异常-疑似软删';inf='单个仍可查 但 列表已移除'}
    else{v='异常-删除未生效';inf='单个可查 且 列表残留'+left.length}
    log(module,'删除后验证',v,inf)
  }
}
const BATCH=[
  {module:'Routing',fields:{code:'RT',name:'自测工艺'}},
]
;(async()=>{
  for(const b of BATCH){try{await run(b.module,b.fields,b.noDelete)}catch(e){log(b.module,'模块异常','异常',e?.message||String(e))}}
  mkdirSync(OUT_DIR,{recursive:true})
  writeFileSync(join(OUT_DIR,'routing.json'),JSON.stringify(results,null,2))
  console.log(JSON.stringify(results,null,2))
})().catch(e=>{console.error('脚本异常',e);process.exit(2)})
