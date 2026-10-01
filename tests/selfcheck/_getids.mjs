const BASE='http://localhost:8080/api'
async function j(p,opt={}){const r=await fetch(BASE+p,{...opt,headers:{'Content-Type':'application/json',...(opt.headers||{})}});let b=null;try{b=await r.json()}catch{};return{status:r.status,ok:r.ok,body:b}}
const login=await j('/Auth/login',{method:'POST',body:{account:'admin',password:'Admin123'}})
const t=login.body?.data?.token; if(!t){console.error('登录失败');process.exit(1)}
const H={Authorization:'Bearer '+t}
for(const m of ['Operation','Unit','DefectItem','Routing']){
  const r=await j('/'+m+'?page=1&pageSize=100',{headers:H})
  const arr=r.body?.data?.list??r.body?.data??[]
  console.log('== '+m+' (count='+(Array.isArray(arr)?arr.length:'-')+') ==')
  if(Array.isArray(arr))arr.forEach(x=>console.log('  id='+x.id+' code='+x.code+' name='+x.name+' seq='+x.seq))
}
