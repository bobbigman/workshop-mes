const fs = require('node:fs');
const {execFileSync: run} = require('node:child_process');
const path = 'frontend/src/components/PwaGuide.vue';
let text = fs.readFileSync(path, 'utf8');
const head = run('git', ['show', 'HEAD:' + path], {encoding:'utf8'});
const oldSteps = head.match(/<p class="step-title">安卓<\/p>\n\s*<p class="step-text">[^\n]+\n\s*<img[^\n]+/g);
let i = 0;
text = text.replace(/<p class="step-title">安卓（华为 \/ 荣耀等自带浏览器）<\/p>\n(\s*)<p class="step-text">[^\n]+\n\s*<img[^\n]+\n\s*<img[^\n]+/g, (_, indent) => {
 const steps = oldSteps[i++].split('\n');
 return steps.map((line, index) => index ? indent + line.trimStart() : line).join('\n');
});
if (i !== 2) throw new Error('原有安卓引导改动隔离失败');
const hash = run('git', ['hash-object','-w','--stdin'], {input:text, encoding:'utf8'}).trim();
run('git', ['update-index','--add','--cacheinfo','100644',hash,path]);