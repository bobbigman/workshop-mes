const fs=require('fs'); const {execFileSync:run}=require('child_process');
for(const path of ['docs/开发手册和方案/01-PRD需求规格说明书.md','docs/开发手册和方案/31-使用手册-工人（手机端）.md']) {
 let s=run('git',['show','HEAD:'+path],{encoding:'utf8'}).replace(/\r\n/g,'\n');
 const w=fs.readFileSync(path,'utf8').replace(/\r\n/g,'\n');
 if(path.includes('01-')) {
  s=s.replace('手机工作台提供直达入口与纵向布局（指令275）','手机工作台通过“更多功能 → 管理操作”菜单提供入口，沿用纵向布局（指令275、278）');
  const line=w.split('\n').find(x=>x.startsWith('- **手机工作台分层（278）**'));
  s=s.replace(/(- 手机默认入口（271）：[^\n]*\n)/,'$1'+line+'\n');
 } else {
  const line=w.split('\n').find(x=>x.startsWith('2. 手机/PDA普通登录后'));
  s=s.replace(/2\. 手机\/PDA普通登录后[^\n]*/,line);
  const more=w.split('\n').find(x=>x.startsWith('完整视角可直接从'));
  s=s.replace(/在【工作台】点【更多功能】[^\n]*/,more);
 }
 const hash=run('git',['hash-object','-w','--stdin'],{input:s,encoding:'utf8'}).trim();run('git',['update-index','--add','--cacheinfo','100644',hash,path]);
}
run('git',['add','--','frontend/src/views/h5/home.vue','frontend/src/views/h5/more.vue','SupportDocs/22-手机工作台代报与补报入口.md','docs/已做指令/278-优化指令-手机工作台彩色八宫格与今日成果前置.md','docs/已做指令/278-工作台设计参考.png','docs/未做指令/278-优化指令-手机工作台彩色八宫格与今日成果前置.md','docs/未做指令/278-工作台设计参考.png','docs/测试方案/278-工作台验证']);