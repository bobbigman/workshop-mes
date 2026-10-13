// 异常处理自测：上报(三类)→列表→处理resolve→非法关联单
import { mkdirSync, writeFileSync } from 'fs'
import { join } from 'path'
const BASE='http://localhost:8080/api'
const OUT_DIR=join(process.cwd(),'_selfcheck_out')
const results=[]
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
const log=(m,s,v,i='')=>results.push({module:m,step:s,status:v,info:i})
async function api(p,{method='GET',token,body}={}){
  for(let a=0;a<3;a++){try{const headers={};if(token)headers['Authorization']='Bearer '+token
    let b=body
    if(body&&!(body instanceof FormData)){headers['Content-Type']='application/json';b=JSON.stringify(body)}
    const r=await fetch(BASE+p,{method,headers,body:b});let j=null;try{j=await r.json()}catch{};if(j)return{status:r.status,ok:r.ok,json:j}}catch{};await sleep(400)}
  return{status:0,ok:false,json:null}
}
async function login(retry=4){for(let i=0;i<retry;i++){try{const r=await api('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}});if(r.ok&&r.json?.data?.token)return r.json.data}catch{};await sleep(700)}throw new Error('登录失败')}
async function writeOut(){mkdirSync(OUT_DIR,{recursive:true});writeFileSync(join(OUT_DIR,'abnormal.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2))}
async function main(){
  const M='Abnormal'
  const u=await login();const t=u.token
  log(M,'登录','通过',u.name)
  // 上报三类
  for(const type of [1,2,3]){
    const fd=new FormData();fd.append('type',String(type));fd.append('description','自测-异常类型'+type)
    const r=await api('/Abnormal',{method:'POST',token:t,body:fd})
    const ok=r.ok&&r.json?.code===0
    log(M,'上报-类型'+type,ok?'通过':'失败','http='+r.status+' code='+(r.json?.code??'-')+' msg='+(r.json?.msg??'')+(ok?'':' 返回='+JSON.stringify(r.json)))
  }
  // 关联不存在工单
  const fd2=new FormData();fd2.append('type','1');fd2.append('description','自测-不存在工单');fd2.append('workOrderId','99999999')
  const r2=await api('/Abnormal',{method:'POST',token:t,body:fd2})
  log(M,'上报-关联不存在工单',(r2.ok&&r2.json?.code===0)?'通过(被接受)':'失败(校验生效)','http='+r2.status+' code='+(r2.json?.code??'-')+' msg='+(r2.json?.msg??'')+' 返回='+JSON.stringify(r2.json))
  // 列表
  const lst=await api('/Abnormal?page=1&pageSize=20',{token:t})
  const rows=lst.json?.data?.list??lst.json?.data??[]
  log(M,'列表查询',(lst.ok&&lst.json?.code===0)?'通过':'失败','count='+(Array.isArray(rows)?rows.length:'-')+' 结构='+JSON.stringify(lst.json?.data).slice(0,120))
  // 处理一条
  const first=(Array.isArray(rows)?rows:[]).find(x=>x.status===0||x.status===undefined||x.resolveStatus===0)
  if(first){
    const res=await api('/Abnormal/'+first.id+'/resolve',{method:'POST',token:t,body:{note:'自测处理'}})
    log(M,'处理resolve#'+first.id,(res.ok&&res.json?.code===0)?'通过':'失败','code='+(res.json?.code??'-')+' msg='+(res.json?.msg??'')+' 返回='+JSON.stringify(res.json).slice(0,150))
  }else{log(M,'处理resolve','跳过(无待处理)','count='+(Array.isArray(rows)?rows.length:'-'))}
  await writeOut()
}
main().catch(async e=>{results.push({module:'Abnormal',step:'模块异常',status:'异常',info:(e?.message||String(e))});await writeOut();process.exit(2)})
