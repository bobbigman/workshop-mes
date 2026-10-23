const fs=require('fs');const p='frontend/src/views/h5/more.vue';let s=fs.readFileSync(p,'utf8').replace(/\r\n/g,'\n');
s=s.replace('v-if="managementActions.length"','v-if="canManage"').replace('<van-action-sheet v-model:show="managementOpen" title="管理操作" :actions="managementActions" cancel-text="取消" close-on-click-action @select="openManagement" />','<H5ManagementMenu v-model:show="managementOpen" />');
s=s.replace("import { computed, ref, onMounted }", "import { ref, onMounted }").replace(', ActionSheet as VanActionSheet','').replace("import { useRouter } from 'vue-router'\nimport { managementDestination, canOpenReportManagement } from '@/utils/reportManagement'", "import H5ManagementMenu from '@/components/H5ManagementMenu.vue'").replace('const router = useRouter()\n','');
s=s.replace(/const managementActions = computed\(\(\) => \{[\s\S]*?(?=const loading =)/,'const canManage = role === 1 || role === 3\n');fs.writeFileSync(p,s);
const files=['docs/开发手册和方案/01-PRD需求规格说明书.md','docs/开发手册和方案/31-使用手册-工人（手机端）.md','SupportDocs/22-手机工作台代报与补报入口.md'];
fs.mkdirSync('.tmp-build/278/adjust-before',{recursive:true});
for(const p of files){let s=fs.readFileSync(p,'utf8');fs.writeFileSync('.tmp-build/278/adjust-before/'+p.split('/').pop(),s);
s=s.replaceAll('更多功能 → 管理操作','工作台 → 管理操作').replaceAll('更多功能→管理操作','工作台→管理操作');
if(p.includes('01-'))s=s.replace('车间看板、帮助、更多功能；','车间看板、帮助；管理员/班组长另显示管理操作。').replace('管理操作迁至更多功能，','管理操作直接位于首页，').replace('工作台保留现场主流程，增加更多功能及帮助入口。更多功能复用生产报表、执行监控、车间看板；','工作台保留现场主流程，直接提供生产报表、执行监控、车间看板及帮助入口，不再显示更多功能按钮；');
if(p.includes('31-'))s=s.replace('【车间看板】【帮助】【更多功能】；','【车间看板】【帮助】；管理员/班组长另有【管理操作】。').replace('点【工作台→管理操作】','在工作台点【管理操作】').replace('## 更多功能与AI操作帮助','## 工作台功能与AI操作帮助').replace('，也可点【更多功能】进入；','；').replace('管理员/班组长在【更多功能】中可点【管理操作】','管理员/班组长在【工作台】可直接点【管理操作】');
fs.writeFileSync(p,s);}