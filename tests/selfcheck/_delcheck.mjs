// 验证删除语义：建→删→查列表是否残留→查单个返回体
const BASE='http://localhost:8080/api'
async function j(p,opt={}){const r=await fetch(BASE+p,{...opt,headers:{'Content-Type':'application/json',...(opt.headers||{})}});let b=null;try{b=await r.json()}catch{};return{status:r.status,ok:r.ok,body:b}}
const login=await j('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}})
const t=login.body.data.token; const H={Authorization:'Bearer '+t}
const code='AUTODELCHK-'+Date.now()
const cr=await j('/Department',{method:'POST',headers:H,body:{code,name:'删除验证部门',memberIds:[]}})
let id=cr.body?.data?.id
if(id==null){const l=await j('/Department?page=1&pageSize=500',{headers:H});id=(l.body.data||[]).find(x=>x.code===code)?.id}
console.log('created id='+id)
// 查单个(删前)
const b1=await j('/Department/'+id,{headers:H});console.log('删前GET status='+b1.status)
// 删除
const del=await j('/Department/'+id,{method:'DELETE',headers:H});console.log('DELETE status='+del.status+' body='+JSON.stringify(del.body))
// 删后查单个
const b2=await j('/Department/'+id,{headers:H});console.log('删后GET单个 status='+b2.status+' body='+JSON.stringify(b2.body))
// 删后查列表是否残留
const l2=await j('/Department?page=1&pageSize=500',{headers:H});const arr=l2.body.data||[];const left=arr.filter(x=>x.code===code)
console.log('删后列表残留='+left.length+(left.length?' '+JSON.stringify(left[0]):''))
