// 报工自测：管理员测规则校验(正常/超剩余/不良原因/时长)，工人张厨测权限；复核通过/退回
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
async function login(acc,pass,retry=4){
  for(let i=0;i<retry;i++){
    try{const r=await api('/Auth/login',{method:'POST',body:{account:acc,password:pass}})
      if(r.ok&&r.json?.data?.token)return r.json.data}catch{}
    await sleep(700)}
  throw new Error('登录失败:'+acc)
}
async function writeOut(){
  mkdirSync(OUT_DIR,{recursive:true}); writeFileSync(join(OUT_DIR,'report.json'),JSON.stringify(results,null,2)); console.log(JSON.stringify(results,null,2))
}
async function main(){
  const M='Report'
  const admin=await login('admin','Admin123'); const at=admin.token
  log(M,'登录-admin','通过',admin.name+' role='+admin.role)
  // 自建工单（createOrder 存在 500 异常，登记；报工改用现有执行中工单）
  const cr=await api('/WorkOrder',{method:'POST',token:at,body:{orderNo:undefined,productId:2,qty:10,dueDate:null,ext:{}}})
  log(M,'新建工单(createOrder)',(cr.ok&&cr.json?.code===0)?'通过':'异常500','code='+(cr.json?.code??'-')+' msg='+(cr.json?.msg??'')+' 完整='+JSON.stringify(cr.json))
  // 用现有工单测报工
  const lk=await api('/WorkOrder?page=1&pageSize=100&excludeCancelled=true',{token:at})
  const arr=lk.json?.data?.list??lk.json?.data??[]
  const wo=(Array.isArray(arr)?arr:[]).find(x=>x.status===1&&(x.remainQty>0||x.doneQty<x.planQty))
  log(M,'取现有执行中工单',wo?'通过':'异常','id='+wo?.id+' no='+wo?.orderNo+' 剩余='+wo?.remainQty)
  if(!wo){await writeOut();return}
  const wid=wo.id
  // 工序：取未报满的工序
  const det=await api('/WorkOrder/'+wid,{token:at})
  const tasks=det.json?.data?.tasks??[]
  const op=(Array.isArray(tasks)?tasks:[]).find(t=>t.operationId&&Number(t.planQty)>0&&Number(t.doneQty)<Number(t.planQty))||(Array.isArray(tasks)?tasks:[]).find(t=>t.operationId)
  log(M,'取未满工序',op?'通过':'异常','opId='+op?.operationId+' opName='+op?.operationName+' plan='+op?.planQty+' done='+op?.doneQty)
  if(!op){await writeOut();return}
  const opId=op.operationId
  async function rep(desc,body){
    const r=await api('/Report',{method:'POST',token:at,body})
    const ok=r.ok&&r.json?.code===0
    const msg=r.json?.msg??r.json?.code
    log(M,desc,ok?'通过':'失败','http='+r.status+' code='+(r.json?.code??'-')+' msg='+msg+' 返回='+JSON.stringify(r.json))
    return {ok,r}
  }
  // 正常报工
  await rep('报工-正常(good3/min30)',{orderId:wid,operationId:opId,goodQty:3,defectQty:0,defectId:null,durationMinutes:30})
  // 超剩余
  await rep('报工-超剩余(good999)',{orderId:wid,operationId:opId,goodQty:999,defectQty:0,defectId:null,durationMinutes:30})
  // 不良不选原因
  await rep('报工-不良无原因(defect2)',{orderId:wid,operationId:opId,goodQty:1,defectQty:2,defectId:null,durationMinutes:30})
  // 时长超24h
  await rep('报工-时长超24h(1441)',{orderId:wid,operationId:opId,goodQty:1,defectQty:0,defectId:null,durationMinutes:1441})
  // 负数量（已知 SubmitAsync 未校验，确证）
  await rep('报工-负数量(good-1)',{orderId:wid,operationId:opId,goodQty:-1,defectQty:0,defectId:null,durationMinutes:30})
  await rep('报工-负不良(defect-1)',{orderId:wid,operationId:opId,goodQty:0,defectQty:-1,defectId:null,durationMinutes:30})
  // 工人权限
  try{
    const wk=await login('zhangchu','Admin123'); const wt=wk.token
    log(M,'登录-zhangchu','通过',wk.name+' role='+wk.role)
    const rw=await api('/Report',{method:'POST',token:wt,body:{orderId:wid,operationId:opId,goodQty:2,defectQty:0,defectId:null,durationMinutes:20}})
    const okw=rw.ok&&rw.json?.code===0
    log(M,'报工-张厨权限',okw?'通过(有权可报)':'失败(无权限/被拒)','code='+(rw.json?.code??'-')+' msg='+(rw.json?.msg??'')+' 返回='+JSON.stringify(rw.json))
  }catch(e){log(M,'登录-zhangchu','异常',e?.message||String(e))}
  // 复核列表 + 复核
  const rv=await api('/Review?page=1&pageSize=20',{token:at})
  log(M,'复核-查询列表',(rv.ok&&rv.json?.code===0)?'通过':'失败','http='+rv.status+' count='+(rv.json?.data?.length??rv.json?.data?.list?.length??'-'))
  await writeOut()
}
main().catch(async e=>{results.push({module:'Report',step:'模块异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
