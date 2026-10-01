// 修复：找到负数报工并用 updateReport 改正为 0，恢复 WorkOrder 列表。登记修复过程。
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
async function writeOut(){mkdirSync(OUT_DIR,{recursive:true});writeFileSync(join(OUT_DIR,'fix.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2))}
async function main(){
  const M='Fix-负数报工'
  const u=await login();const t=u.token
  // 查报工列表找负数
  const rl=await api('/Report?page=1&pageSize=200',{token:t})
  const rows=rl.json?.data?.list??rl.json?.data??[]
  const neg=(Array.isArray(rows)?rows:[]).filter(x=>Number(x.goodQty)<0||Number(x.defectQty)<0)
  log(M,'查询负数报工',neg.length>0?'发现'+neg.length+'条':'无负数','count='+(Array.isArray(rows)?rows.length:'-'))
  for(const n of neg){
    const body={goodQty:Number(n.goodQty)<0?0:Number(n.goodQty),defectQty:Number(n.defectQty)<0?0:Number(n.defectQty),operationId:n.operationId,orderId:n.orderId}
    const up=await api('/Report/'+n.id,{method:'PUT',token:t,body})
    log(M,'修复报工#'+n.id,(up.ok&&up.json?.code===0)?'通过':'失败','原good='+n.goodQty+' 改0 http='+up.status+' code='+(up.json?.code??'-')+' msg='+(up.json?.msg??'')+' 返回='+JSON.stringify(up.json).slice(0,200))
  }
  // 验证工单列表恢复
  await sleep(400)
  const lk=await api('/WorkOrder?page=1&pageSize=5&excludeCancelled=true',{token:t})
  log(M,'验证工单列表恢复',(lk.ok&&lk.json?.code===0)?'通过(已恢复)':'仍失败','code='+(lk.json?.code??'-')+' msg='+(lk.json?.msg??'')+' total='+(lk.json?.data?.total??'-'))
  await writeOut()
}
main().catch(async e=>{results.push({module:'Fix',step:'异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
