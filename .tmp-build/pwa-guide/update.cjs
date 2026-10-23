const fs = require('node:fs');
const path = 'frontend/src/components/PwaGuide.vue';
let text = fs.readFileSync(path, 'utf8').replace(/\r\n/g, '\n');
const oldActions = `      <div class="actions">
        <button type="button" class="btn-secondary" @click="emit('dismiss-forever')">不再提示</button>
        <button type="button" class="btn-primary" @click="emit('dismiss-this-time')">知道了</button>
      </div>`;
if (!text.includes(oldActions)) throw new Error('找不到原按钮区域');
text = text.replace('\n' + oldActions, '      </div>');
const start = text.indexOf('      <h3');
const end = text.indexOf('      <div v-if="isAndroid"');
text = text.slice(0, start) + `      <div class="guide-header">
        <h3 id="pwa-guide-title">添加到手机桌面</h3>
        <p class="lead">装一次，以后点桌面图标就能进，不用每次输网址。</p>
        <div class="actions">
          <button type="button" class="btn-secondary" @click="emit('dismiss-forever')">以后不再自动弹出</button>
          <button type="button" class="btn-primary" @click="emit('dismiss-this-time')">只关闭本次</button>
        </div>
      </div>

      <div class="guide-content">
` + text.slice(end);
const bodyStart = text.indexOf('      <div class="guide-content">\n') + '      <div class="guide-content">\n'.length;
const bodyEnd = text.indexOf('\n      </div>\n    </div>\n  </div>\n</template>', bodyStart);
text = text.slice(0, bodyStart) + text.slice(bodyStart, bodyEnd).split('\n').map(line => line ? '  ' + line : line).join('\n') + text.slice(bodyEnd);
text = text.replace('role="dialog" aria-labelledby', 'role="dialog" aria-modal="true" aria-labelledby');
text = text.replace('  overflow-y: auto;\n  background:', '  max-height: min(88dvh, 640px);\n  display: flex;\n  flex-direction: column;\n  overflow: hidden;\n  background:');
text = text.replace('  padding: 20px 16px 16px;\n', '');
text = text.replace('\nh3 {', `\n.guide-header {
  flex-shrink: 0;
  padding: 20px 16px 14px;
  border-bottom: 1px solid var(--app-border, #e5e6eb);
}
.guide-content {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
  padding: 14px 16px 16px;
}
h3 {`);
text = text.replace('  margin-top: 8px;', '  margin-top: 0;');
text = text.replace('  height: 44px;', '  min-width: 0;\n  min-height: 44px;\n  padding: 8px 6px;\n  line-height: 1.4;\n  white-space: normal;\n  overflow-wrap: anywhere;');
fs.writeFileSync(path, text);