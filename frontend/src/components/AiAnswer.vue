<script setup>
import { computed } from 'vue'
import AiInline from './AiInline.vue'
import { parseAiMarkdown } from '@/utils/aiMarkdown'

const props = defineProps({
  text: { type: String, default: '' }
})

const blocks = computed(() => parseAiMarkdown(props.text))
</script>

<template>
  <div class="ai-md">
    <template v-for="(block, index) in blocks" :key="index">
      <component :is="`h${block.level}`" v-if="block.type === 'h'" class="ai-md-h" :class="{ 'is-focus': block.parts.some(part => /重点关注|优先处理/.test(part.text || '')) }">
        <AiInline :parts="block.parts" />
      </component>

      <p
        v-else-if="block.type === 'p'"
        class="ai-md-p"
        :class="block.tone ? `is-${block.tone}` : ''"
      >
        <AiInline :parts="block.parts" />
      </p>

      <ul v-else-if="block.type === 'ul'" class="ai-md-list">
        <li v-for="(item, itemIndex) in block.items" :key="itemIndex">
          <AiInline :parts="item" />
        </li>
      </ul>

      <ol v-else-if="block.type === 'ol'" class="ai-md-list" :start="block.start">
        <li v-for="(item, itemIndex) in block.items" :key="itemIndex">
          <AiInline :parts="item" />
        </li>
      </ol>

      <div v-else-if="block.type === 'table'" class="ai-md-table-wrap">
        <table class="ai-md-table">
          <thead>
            <tr>
              <th
                v-for="(cell, cellIndex) in block.head"
                :key="cellIndex"
                :style="{ textAlign: block.align[cellIndex] || 'left' }"
              >
                <AiInline :parts="cell" />
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(row, rowIndex) in block.rows" :key="rowIndex">
              <td
                v-for="(cell, cellIndex) in row"
                :key="cellIndex"
                :style="{ textAlign: block.align[cellIndex] || 'left' }"
              >
                <AiInline :parts="cell" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <blockquote v-else-if="block.type === 'quote'" class="ai-md-quote">
        <p v-for="(line, lineIndex) in block.lines" :key="lineIndex">
          <AiInline :parts="line" />
        </p>
      </blockquote>

      <pre v-else-if="block.type === 'code'" class="ai-md-pre"><code>{{ block.text }}</code></pre>

      <hr v-else-if="block.type === 'hr'" class="ai-md-hr" />
    </template>
  </div>
</template>

<!-- 不用 scoped：Markdown 内容在 AiInline 子组件里，scoped 选择器穿不过去；统一用 .ai-md 前缀限定作用域。 -->
<style>
.ai-md {
  font-size: 16px;
  line-height: 1.9;
  color: var(--app-text-1, #1d2129);
  word-break: break-word;
}
.ai-md > :first-child { margin-top: 0; }
.ai-md > :last-child { margin-bottom: 0; }

.ai-md-h {
  margin: 32px 0 14px;
  font-size: 19px;
  line-height: 1.45;
  font-weight: 600;
  color: var(--app-text-1, #1d2129);
}
h2.ai-md-h, h1.ai-md-h {
  padding: 0 0 12px;
  border-bottom: 1px solid #e6eaf0;
  font-size: 20px;
}
h3.ai-md-h, h4.ai-md-h, h5.ai-md-h, h6.ai-md-h {
  font-size: 18px;
  color: var(--app-text-1, #1d2129);
}
.ai-md-h.is-focus { color: #925319; margin-bottom: 10px; }
.ai-md-h.is-focus + .ai-md-list {
  padding: 12px 20px 12px 40px; border: 1px solid #f1e1c9;
  border-radius: 10px; background: #fffaf1;
}
.ai-md-h.is-focus + .ai-md-list li { margin: 6px 0; }

.ai-md-p { margin: 0 0 16px; }
.ai-md > .ai-md-p:first-child:not(.is-warn) {
  font-size: 17px; font-weight: 500; line-height: 1.85;
  margin-bottom: 26px; color: #26364d;
}
.ai-md-p.is-lead {
  margin-bottom: 13px;
  padding: 10px 12px;
  border: 1px solid #dbe7ff;
  border-left: 3px solid var(--app-primary, #3370ff);
  border-radius: 8px;
  background: #f2f7ff;
}
.ai-md-p.is-warn {
  margin-bottom: 12px;
  padding: 9px 12px;
  border: 1px solid #ffe2b8;
  border-radius: 8px;
  background: #fff8e8;
}

.ai-md strong {
  font-weight: 600;
  color: var(--app-text-1, #1d2129);
}
.ai-md a {
  color: var(--app-primary, #3370ff);
  text-decoration: none;
  border-bottom: 1px dashed currentColor;
}
.ai-md a:hover { color: var(--app-primary-hover, #245bdb); }

.ai-md-code-inline {
  padding: 1px 5px;
  border-radius: 4px;
  background: #f2f3f5;
  color: #b45309;
  font-family: var(--app-font-mono, Consolas, monospace);
  font-size: 12.5px;
}
.ai-md-src {
  display: inline-block;
  margin: 0 2px;
  padding: 0 5px;
  border-radius: 3px;
  background: #f2f3f5;
  color: var(--app-text-3, #86909c);
  font-family: var(--app-font-mono, Consolas, monospace);
  font-size: 12px;
  border: 1px solid #e2e8f0;
  cursor: pointer;
  line-height: 18px;
  overflow-wrap: anywhere;
}
.ai-md-src:hover, .ai-md-src:focus-visible { color: #245bdb; border-color: #8daff8; }
.ai-md-source-detail { font-size: 12px; color: #66758a; overflow-wrap: anywhere; padding: 0 6px; }

.ai-md-list {
  margin: 0 0 20px;
  padding-left: 25px;
}
.ai-md-list li { margin: 8px 0; padding-left: 4px; }
.ai-md-list li::marker { color: var(--app-text-3, #86909c); }

.ai-md-quote {
  margin: 10px 0 12px;
  padding: 9px 12px;
  border-left: 3px solid #c9cdd4;
  border-radius: 0 6px 6px 0;
  background: #f7f8fa;
  color: var(--app-text-2, #4e5969);
}
.ai-md-quote p { margin: 0 0 4px; }
.ai-md-quote p:last-child { margin-bottom: 0; }

.ai-md-table-wrap {
  margin: 10px 0 13px;
  overflow-x: auto;
  border: 1px solid var(--app-border, #e5e6eb);
  border-radius: 8px;
  background: #fff;
}
.ai-md-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 15px;
  line-height: 1.7;
}
.ai-md-table th {
  padding: 13px 16px;
  border-bottom: 1px solid var(--app-border, #e5e6eb);
  background: #f7f8fa;
  color: var(--app-text-2, #4e5969);
  font-weight: 600;
  white-space: nowrap;
}
.ai-md-table td {
  padding: 12px 16px;
  border-top: 1px solid var(--app-border-light, #f2f3f5);
  vertical-align: top;
  min-width: 76px;
}
.ai-md-table tbody tr:first-child td { border-top: 0; }
.ai-md-table td:first-child { white-space: nowrap; }
.ai-md-table tbody tr:nth-child(even) td { background: #fbfcfd; }

.ai-md-pre {
  margin: 10px 0 13px;
  padding: 10px 12px;
  overflow-x: auto;
  border: 1px solid var(--app-border, #e5e6eb);
  border-radius: 8px;
  background: #f7f8fa;
}
.ai-md-pre code {
  padding: 0;
  background: none;
  color: var(--app-text-1, #1d2129);
  font-family: var(--app-font-mono, Consolas, monospace);
  font-size: 12.5px;
  line-height: 1.6;
}

.ai-md-hr {
  margin: 14px 0;
  border: 0;
  border-top: 1px solid var(--app-border, #e5e6eb);
}
@media (max-width: 600px) {
  .ai-md { font-size: 15px; }
  .ai-md > .ai-md-p:first-child:not(.is-warn) { font-size: 16px; }
  h2.ai-md-h, h1.ai-md-h { font-size: 18px; }
  .ai-md-table th, .ai-md-table td { padding: 7px 9px; }
}
</style>
