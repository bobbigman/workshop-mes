const fs=require('fs');const {execFileSync:run}=require('child_process');
for(const p of ['docs/开发手册和方案/01-PRD需求规格说明书.md','docs/开发手册和方案/31-使用手册-工人（手机端）.md']){
 let s=run('git',['show','HEAD:'+p],{encoding:'utf8'});
 s=s.replaceAll('更多功能 → 管理操作','工作台 → 管理操作').replaceAll('更多功能→管理操作','工作台→管理操作');
 if(p.includes('01-'))s=s.replace('车间看板、帮助、更多功能；','车间看板、帮助；管理员/班组长另显示管理操作。').replace('管理操作迁至更多功能，','管理操作直接位于首页，').replace('工作台保留现场主流程，增加更多功能及帮助入口。更多功能复用生产报表、执行监控、车间看板；','工作台保留现场主流程，直接提供生产报表、执行监控、车间看板及帮助入口，不再显示更多功能按钮；').replace('手机工作台通过“工作台 → 管理操作”菜单提供入口','手机工作台通过“管理操作”菜单提供入口');
 else s=s.replace('【车间看板】【帮助】【更多功能】；','【车间看板】【帮助】；管理员/班组长另有【管理操作】。').replace('点【工作台→管理操作】','在工作台点【管理操作】').replace('## 更多功能与AI操作帮助','## 工作台功能与AI操作帮助').replace('，也可点【更多功能】进入；','；').replace('管理员/班组长在【更多功能】中可点【管理操作】','管理员/班组长在【工作台】可直接点【管理操作】');
 const hash=run('git',['hash-object','-w','--stdin'],{input:s,encoding:'utf8'}).trim();run('git',['update-index','--add','--cacheinfo','100644',hash,p]);
}
const instruction='docs/已做指令/278-优化指令-手机工作台彩色八宫格与今日成果前置.md';fs.appendFileSync(instruction,'\n追加变更验证：2026-10-23 构建通过；原管理权限/目的路由/计时校验3项测试通过；manual:sync与manual:check通过。本次小改未重新做浏览器或真机验收，未部署。\n');
run('git',['add','--','frontend/src/components/H5ManagementMenu.vue','frontend/src/views/h5/home.vue','frontend/src/views/h5/more.vue','SupportDocs/21-手机更多功能与AI操作帮助.md','SupportDocs/22-手机工作台代报与补报入口.md',instruction]);