const {execFileSync: run} = require('node:child_process');
const changes = [
 ['frontend/index.html', s => s.replace('name="apple-mobile-web-app-title" content="小工单"', 'name="apple-mobile-web-app-title" content="微聚"')],
 ['frontend/src/components/PwaGuide.vue', s => s.replace('alt="桌面图标示意"', 'alt="手机桌面上的微聚图标，红框标出位置"').replace('max-width: 160px;', 'max-width: 280px;')],
 ['docs/开发手册和方案/31-使用手册-工人（手机端）.md', s => s.replaceAll('【小工单】','【微聚】').replace('![图：装好后桌面应出现「小工单」图标（示意）。]', '![图：装好后桌面出现「微聚」图标，红框标出位置。]')]
];
for (const [path, edit] of changes) {
 const before = run('git', ['show', 'HEAD:' + path], {encoding:'utf8'});
 const after = edit(before);
 if (before === after) throw new Error('Expected change missing: ' + path);
 const hash = run('git', ['hash-object','-w','--stdin'], {input:after, encoding:'utf8'}).trim();
 run('git', ['update-index','--add','--cacheinfo','100644',hash,path]);
}
run('git',['add','--','frontend/public/manual/pwa-honor-add.png','frontend/public/manual/pwa-homescreen.png']);