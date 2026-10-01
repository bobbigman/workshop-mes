const BASE='http://localhost:8080/api'
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms))
async function j(p,opt={}){const r=await fetch(BASE+p,{...opt,headers:{'Content-Type':'application/json',...(opt.headers||{})}});let b=null;try{b=await r.json()}catch{};return{status:r.status,ok:r.ok,body:b}}
async function login(retry=3){for(let i=0;i<retry;i++){try{const r=await j('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}});if(r.ok&&r.body?.data?.token)return r.body.data}catch{};await sleep(500)}throw new Error('登录失败')}
const u=await login();const H={Authorization:'Bearer '+u.token}
console.log('=== 新增前列表(前3条) ===')
let r1=await j('/WorkOrder?page=1&pageSize=50&excludeCancelled=true',{headers:H});let a1=r1.body?.data?.list??[];console.log('total='+r1.body?.data?.total)
a1.slice(0,3).forEach(x=>console.log('  orderNo='+x.orderNo+' id='+x.id+' productId='+x.productId+' planQty='+x.planQty+' qty='+x.qty+' status='+x.status+' due='+x.dueDate))
console.log('=== POST 新增 qty:5 ===')
const cr=await j('/WorkOrder',{method:'POST',headers:H,body:{orderNo:undefined,productId:2,qty:5,dueDate:null,ext:{}}})
console.log('返回 code='+cr.body?.code+' msg='+cr.body?.msg+' data='+JSON.stringify(cr.body?.data))
await sleep(400)
console.log('=== 新增后列表(前3条) ===')
let r2=await j('/WorkOrder?page=1&pageSize=50&excludeCancelled=true',{headers:H});let a2=r2.body?.data?.list??[];console.log('total='+r2.body?.data?.total)
a2.slice(0,3).forEach(x=>console.log('  orderNo='+x.orderNo+' id='+x.id+' productId='+x.productId+' planQty='+x.planQty+' qty='+x.qty+' status='+x.status))
console.log('=== 新增后全量(不含取消)找 productId=2 且 plan/qty=5 ===')
a2.filter(x=>x.productId===2&&(x.planQty===5||x.qty===5)).forEach(x=>console.log('  命中 orderNo='+x.orderNo+' id='+x.id))
