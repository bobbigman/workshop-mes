// 清理测试数据：删除指定资源下 code 以 AUTO 开头的对象。用法: node _cleanup.mjs <资源名>
const BASE='http://localhost:8080/api'
async function j(p,opt={}){const r=await fetch(BASE+p,{...opt,headers:{'Content-Type':'application/json',...(opt.headers||{})}});let b=null;try{b=await r.json()}catch{};return{status:r.status,ok:r.ok,body:b}}
const mod=process.argv[2]
const login=await j('/Auth/login',{method:'POST',body:{account:process.env.SELFTEST_USER||'admin',password:process.env.SELFTEST_PASS||'Admin123'}})
console.error('login status='+login.status+' body='+JSON.stringify(login.body).slice(0,200))
const t=login.body?.data?.token
if(!t){console.error('登录失败');process.exit(1)}
const H={Authorization:'Bearer '+t}
const lst=await j('/'+mod+'?page=1&pageSize=500',{headers:H})
const arr=lst.body?.data?.list ?? lst.body?.data ?? []
const targets=(Array.isArray(arr)?arr:[]).filter(x=>String(x.code||'').toUpperCase().startsWith('AUTO'))
console.log('待清理 '+mod+': '+targets.length+' 条')
for(const x of targets){
  const r=await j('/'+mod+'/'+x.id,{method:'DELETE',headers:H})
  console.log('  删除 id='+x.id+' code='+x.code+' http='+r.status+' code='+(r.body?.code??'-'))
}
