// 复核退回(reject)补测：造一笔报工→进待复核→reject退回
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'
const BASE='http://localhost:8080/api'
const OUT_DIR=join(process.cwd(),'_selfcheck_out')
const results=[]
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
const log=(m,s,v,i='')=>results.push({module:m,step:s,status:v,info:i})
async function api(p,{method='GET',token,body}={}){
  for(let a=0;a<3;a++){try{const headers={'Content-Type':'application/json'};if(token)headers['Authorization']='Bearer '+token
    const r=await fetch(BASE+p,{method,headers,body:body?JSON.stringify(body):undefined});let j=null;try{j=await r.json()}catch{};if(j)return{status:r.status,ok:r.ok,json:j}}catch{};await sleep(400)}
  return{status:0,ok:false,json:null}
}
async function login(retry=4){for(let i=0;i<retry;i++){try{const r=await api('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}});if(r.ok&&r.json?.data?.token)return r.json.data}catch{};await sleep(700)}throw new Error('登录失败')}
async function writeOut(){mkdirSync(OUT_DIR,{recursive:true});writeFileSync(join(OUT_DIR,'reject.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2))}
async function main(){
  const M='Review-Reject'
  const u=await login();const t=u.token
  // 找未满工单+工序造报工
  const lk=await api('/WorkOrder?page=1&pageSize=50&excludeCancelled=true',{token:t})
  const arr=lk.json?.data?.list??lk.json?.data??[]
  let wo=null,op=null
  for(const w of (Array.isArray(arr)?arr:[])){
    const d=await api('/WorkOrder/'+w.id,{token:t})
    const tasks=d.json?.data?.tasks??[]
    const cand=(Array.isArray(tasks)?tasks:[]).find(o=>o.operationId&&Number(o.planQty)>0&&Number(o.doneQty)<Number(o.planQty))
    if(cand){wo=w;op=cand;break}
  }
  log(M,'找未满工单+工序',wo?'通过':'异常','woId='+wo?.id+' op='+op?.operationName+'('+op?.operationId+') done='+op?.doneQty+'/'+op?.planQty)
  if(!wo||!op){await writeOut();return}
  const rep=await api('/Report',{method:'POST',token:t,body:{orderId:wo.id,operationId:op.operationId,goodQty:1,defectQty:0,defectId:null,durationMinutes:10}})
  const repOk=rep.ok&&rep.json?.code===0
  log(M,'造报工(good1)',repOk?'通过':'失败','code='+(rep.json?.code??'-')+' msg='+(rep.json?.msg??'')+' 返回='+JSON.stringify(rep.json).slice(0,150))
  const pend=await api('/Review/pending',{token:t})
  const pd=pend.json?.data;const pl=(Array.isArray(pd)?pd:(pd?.list??[]))
  log(M,'待复核-查询',(pend.ok&&pend.json?.code===0)?'通过':'失败','count='+(Array.isArray(pl)?pl.length:'-'))
  const mine=(Array.isArray(pl)?pl:[]).find(x=>String(x.workOrderId)===String(wo.id)&&String(x.operationId)===String(op.operationId)&&x.userName==='总管理员')
  if(mine){
    const rj=await api('/Review/'+mine.id+'/reject',{method:'POST',token:t,body:{reason:'自测退回'}})
    log(M,'复核-退回reject#'+mine.id,(rj.ok&&rj.json?.code===0)?'通过':'失败','code='+(rj.json?.code??'-')+' msg='+(rj.json?.msg??'')+' 返回='+JSON.stringify(rj.json).slice(0,150))
  }else{log(M,'复核-退回reject','异常-未找到该笔待复核','pl='+JSON.stringify((Array.isArray(pl)?pl:[]).slice(0,3)).slice(0,200))}
  await writeOut()
}
main().catch(async e=>{results.push({module:'Review-Reject',step:'模块异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
