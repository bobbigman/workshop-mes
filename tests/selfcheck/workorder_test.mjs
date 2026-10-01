// 工单全流程自测：新增→查→改→状态流转(start/finish/withdraw/cancel/restore)→复制→删除→软删验证
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'
const BASE='http://localhost:8080/api'
const OUT_DIR=join(process.cwd(),'_selfcheck_out')
const results=[]
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
const log=(m,s,v,i='')=>results.push({module:m,step:s,status:v,info:i})
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
    await sleep(600)}
  throw new Error('登录失败')
}
async function writeOut(){
  mkdirSync(OUT_DIR,{recursive:true})
  writeFileSync(join(OUT_DIR,'workorder.json'),JSON.stringify(results,null,2))
  console.log(JSON.stringify(results,null,2))
}
async function main(){
  const u=await login(); const token=u.token; const M='WorkOrder'
  log(M,'登录','通过',u.name+' role='+u.role)
  // 产品id
  const prd=await api('/Product?page=1&pageSize=100',{token})
  const prds=prd.json?.data?.list??prd.json?.data??[]
  const pid=(Array.isArray(prds)?prds:[]).find(p=>p.id)?.id
  log(M,'前置-取产品',pid!=null?'通过':'异常','productId='+pid+' 返回='+JSON.stringify(prd.json).slice(0,300))
  if(pid==null){await writeOut();return}
  // 新增
  const cr=await api('/WorkOrder',{method:'POST',token,body:{orderNo:undefined,productId:pid,qty:5,dueDate:null,ext:{}}})
  const crOk=cr.ok&&cr.json?.code===0
  const data=cr.json?.data??{}
  let id=data.id, orderNo=data.orderNo
  log(M,'新增工单',crOk?'通过':'失败','http='+cr.status+' code='+(cr.json?.code??'-')+' 完整返回='+JSON.stringify(cr.json))
  // 新增后从列表回查新工单 id（自动生成 orderNo）
  if(id==null||orderNo==null){
    const lk=await api('/WorkOrder?page=1&pageSize=10',{token})
    const arr=lk.json?.data?.list??lk.json?.data??[]
    log(M,'新增-列表快照','—',(Array.isArray(arr)?arr:[]).slice(0,5).map(x=>'no='+x.orderNo+'/id='+x.id+'/pid='+x.productId+'/plan='+x.planQty+'/qty='+x.qty+'/st='+x.status).join(' | '))
    const newest=(Array.isArray(arr)?arr:[]).find(x=>(x.planQty===5||x.qty===5)&&!x.dueDate)
    if(newest){id=id??newest.id; orderNo=orderNo??newest.orderNo}
  }
  log(M,'新增-回查id',id!=null?'通过':'异常','id='+id+' orderNo='+orderNo)
  if(!crOk||id==null){await writeOut();return}
  // 查单个
  const one=await api('/WorkOrder/'+id,{token})
  log(M,'查询单个',(one.ok&&one.json?.code===0)?'通过':'失败','status='+one.json?.data?.status+' qty='+one.json?.data?.qty)
  // 编辑
  const up=await api('/WorkOrder/'+id,{method:'PUT',token,body:{orderNo,productId:pid,qty:8,dueDate:null,ext:{}}})
  log(M,'编辑工单',(up.ok&&up.json?.code===0)?'通过':'失败','http='+up.status+' code='+(up.json?.code??'-'))
  // 状态流转：start
  async function trans(action){
    const r=await api('/WorkOrder/'+id+'/transition',{method:'POST',token,body:{action}})
    const ok=r.ok&&r.json?.code===0
    const after=await api('/WorkOrder/'+id,{token})
    log(M,'流转-'+action,ok?'通过':'失败','code='+(r.json?.code??'-')+' 后status='+after.json?.data?.status+(ok?'':' 返回='+JSON.stringify(r.json)))
  }
  await trans('start')
  await trans('finish')
  await trans('withdraw')
  await trans('cancel')
  await trans('restore')
  // 复制
  const cp=await api('/WorkOrder/'+id+'/copy',{method:'POST',token})
  const cpOk=cp.ok&&cp.json?.code===0
  const cpid=cp.json?.data?.id
  log(M,'复制工单',cpOk?'通过':'失败','http='+cp.status+' code='+(cp.json?.code??'-')+' 新id='+cpid+(cpOk?'':' 返回='+JSON.stringify(cp.json)))
  // 删除（若复制成功删复制单，再删原单）
  if(cpOk&&cpid!=null){
    const d2=await api('/WorkOrder/'+cpid,{method:'DELETE',token})
    log(M,'删除-复制单',(d2.ok&&d2.json?.code===0)?'通过':'失败','code='+(d2.json?.code??'-'))
  }
  const del=await api('/WorkOrder/'+id,{method:'DELETE',token})
  log(M,'删除-原单',(del.ok&&del.json?.code===0)?'通过':'失败','code='+(del.json?.code??'-'))
  const chk=await api('/WorkOrder/'+id,{token})
  const la=await api('/WorkOrder?page=1&pageSize=500',{token})
  const arr=la.json?.data?.list??la.json?.data??[]
  const left=(Array.isArray(arr)?arr:[]).filter(x=>String(x.id)===String(id)||x.orderNo===orderNo)
  const gone = chk.ok && chk.json?.code !== 0 // 业务 code!=0 视为不存在（勿按 HTTP 状态）
  log(M,'删除后验证',(gone&&left.length===0)?'通过(已删干净)':(!gone&&left.length===0?'异常-疑似软删':'异常-删除未生效'),'单个code='+(chk.json?.code??'-')+' 列表残留='+left.length)
  await writeOut()
}
main().catch(async e=>{results.push({module:'WorkOrder',step:'模块异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
