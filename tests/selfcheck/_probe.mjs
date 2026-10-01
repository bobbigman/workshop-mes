const BASE='http://localhost:8080/api'
async function j(p,opt={}){const r=await fetch(BASE+p,{...opt,headers:{'Content-Type':'application/json',...(opt.headers||{})}});let b=null;try{b=await r.json()}catch{};return{status:r.status,ok:r.ok,body:b}}
const login=await j('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}})
const t=login.body.data.token
const H={Authorization:'Bearer '+t}
console.log('== 新增部门原始返回 ==')
const cr=await j('/Department',{method:'POST',headers:H,body:{code:'AUTOPROBE-'+Date.now(),name:'探针部门',memberIds:[]}})
console.log(JSON.stringify(cr,null,2))
console.log('== 部门列表(前3条字段) ==')
const lst=await j('/Department',{headers:H})
const d=lst.body.data
console.log('type=',Array.isArray(d)?'array':'object','len=',Array.isArray(d)?d.length:Object.keys(d||{}).length)
if(Array.isArray(d)){console.log('首条keys=',Object.keys(d[0]||{}).join(','));console.log('首条=',JSON.stringify(d[0]))}
