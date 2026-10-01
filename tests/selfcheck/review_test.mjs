// 复核自测：待复核列表 + 通过(approve) + 退回(reject)
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'
const BASE='http://localhost:8080/api'
const OUT_DIR=join(process.cwd(),'_selfcheck_out')
const results=[]
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
const log=(m,s,v,i='')=>results.push({module:m,step:s,status:v,info:i})
async function api(p,{method='GET',token,body}={}){
  for(let a=0;a<3;a++){
    try{
      const headers={'Content-Type':'application/json'}; if(token)headers['Authorization']='Bearer '+token
      const r=await fetch(BASE+p,{method,headers,body:body?JSON.stringify(body):undefined})
      let j=null; try{j=await r.json()}catch{}
      if(j)return{status:r.status,ok:r.ok,json:j}
    }catch{}
    await sleep(400)
  }
  return{status:0,ok:false,json:null}
}
async function login(acc,pass,retry=4){
  for(let i=0;i<retry;i++){
    try{const r=await api('/Auth/login',{method:'POST',body:{account:acc,password:pass}})
      if(r.ok&&r.json?.data?.token)return r.json.data}catch{}
    await sleep(700)}
  throw new Error('登录失败:'+acc)
}
async function writeOut(){mkdirSync(OUT_DIR,{recursive:true});writeFileSync(join(OUT_DIR,'review.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2))}
async function main(){
  const M='Review'
  const u=await login('admin','Admin123'); const t=u.token
  log(M,'登录','通过',u.name)
  // 待复核列表结构
  const pend=await api('/Review/pending',{token:t})
  const pd=pend.json?.data
  log(M,'待复核-列表结构','—','http='+pend.status+' code='+(pend.json?.code??'-')+' dataType='+(Array.isArray(pd)?'array':typeof pd)+' 前1条='+JSON.stringify((Array.isArray(pd)?pd[0]:pd)).slice(0,200))
  let pendList=(Array.isArray(pd)?pd:(pd?.list??[]))
  log(M,'待复核-数量','—','count='+pendList.length)
  // 找一条可复核的，approve
  const target=pendList[0]
  if(target){
    const ap=await api('/Review/'+target.id+'/approve',{method:'POST',token:t})
    log(M,'复核-通过approve',(ap.ok&&ap.json?.code===0)?'通过':'失败','code='+(ap.json?.code??'-')+' msg='+(ap.json?.msg??'')+' id='+target.id)
  }else{log(M,'复核-通过approve','跳过(无待复核)','')}
  // 造一笔待复核：用现有未满工单报 good1
  const lk=await api('/WorkOrder?page=1&pageSize=100&excludeCancelled=true',{token:t})
  const arr=lk.json?.data?.list??lk.json?.data??[]
  log(M,'工单列表快照','—',(Array.isArray(arr)?arr:[]).slice(0,8).map(x=>'no='+x.orderNo+'/st='+x.status+'/plan='+x.planQty+'/done='+x.doneQty+'/remain='+x.remainQty).join(' | '))
  const wo=(Array.isArray(arr)?arr:[]).find(x=>x.remainQty>0||x.doneQty<x.planQty)
  log(M,'取可报工单',wo?'通过':'异常','id='+wo?.id+' no='+wo?.orderNo+' 剩余='+wo?.remainQty)
  if(wo){
    const det=await api('/WorkOrder/'+wo.id,{token:t})
    const tasks=det.json?.data?.tasks??[]
    const op=(Array.isArray(tasks)?tasks:[]).find(o=>o.operationId&&Number(o.planQty)>0&&Number(o.doneQty)<Number(o.planQty))
    if(op){
      const rep=await api('/Report',{method:'POST',token:t,body:{orderId:wo.id,operationId:op.operationId,goodQty:1,defectQty:0,defectId:null,durationMinutes:10}})
      const repOk=rep.ok&&rep.json?.code===0
      log(M,'造报工(供复核)',repOk?'通过':'失败','code='+(rep.json?.code??'-')+' msg='+(rep.json?.msg??'')+' op='+op.operationName)
      // 找到该笔复核并 reject
      await sleep(300)
      const pend2=await api('/Review/pending',{token:t})
      const pd2=pend2.json?.data
      const pl2=(Array.isArray(pd2)?pd2:(pd2?.list??[]))
      const mine=(Array.isArray(pl2)?pl2:[]).find(x=>String(x.workOrderId)===String(wo.id)&&String(x.operationId)===String(op.operationId))
      if(mine){
        const rj=await api('/Review/'+mine.id+'/reject',{method:'POST',token:t,body:{reason:'自测退回'}})
        log(M,'复核-退回reject',(rj.ok&&rj.json?.code===0)?'通过':'失败','code='+(rj.json?.code??'-')+' msg='+(rj.json?.msg??'')+' id='+mine.id)
      }else{log(M,'复核-退回reject','异常-未找到该笔待复核','pl2 count='+pl2.length)}
    }else{log(M,'造报工','异常-无可报工序','')}
  }
  await writeOut()
}
main().catch(async e=>{results.push({module:'Review',step:'模块异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
