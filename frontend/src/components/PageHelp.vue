<template>
  <el-drawer
    v-model="open"
    :title="drawerTitle"
    direction="rtl"
    size="400px"
    append-to-body
    class="page-help-drawer"
  >
    <template v-if="help">
      <div v-if="help.hook" class="page-help-hero">
        <div class="page-help-hook">▸ {{ help.hook }}</div>
        <template v-if="help.slogan">
          <p
            v-for="(s, i) in sloganLines"
            :key="i"
            class="page-help-slogan"
            :class="{ 'is-solution': i === sloganLines.length - 1 && Array.isArray(help.slogan) }"
            v-html="highlightRich(s)"
          />
        </template>
        <p v-if="help.note" class="help-note" v-html="highlightNote(help.note)" />
      </div>
      <div v-else class="page-help-value" v-html="highlightOps(help.value)" />
      <ul class="page-help-list">
        <li v-for="(line, i) in mustLines" :key="'m' + i">
          <template v-if="line.what">
            <span class="help-op">{{ line.what }}</span
            ><template v-if="line.how">：<span v-html="highlightOps(line.how)" /></template>
          </template>
          <template v-else><span v-html="highlightOps(line.how)" /></template>
          <span v-if="line.who" class="help-perm">（{{ line.who }}）</span>
        </li>
      </ul>
      <div v-if="tipLines.length" class="page-help-tips">
        <button type="button" class="page-help-tips-toggle" @click="tipsOpen = !tipsOpen">
          {{ tipsOpen ? '收起' : '展开' }}避坑提醒（{{ tipLines.length }} 条，点开看）
        </button>
        <ul v-show="tipsOpen" class="page-help-list page-help-tips-list">
          <li v-for="(line, i) in tipLines" :key="'t' + i">
            <template v-if="line.what">
              <span class="help-op">{{ line.what }}</span
              ><template v-if="line.how">：<span v-html="highlightOps(line.how)" /></template>
            </template>
            <template v-else><span v-html="highlightOps(line.how)" /></template>
            <span v-if="line.who" class="help-perm">（{{ line.who }}）</span>
          </li>
        </ul>
      </div>
    </template>
    <template v-else>
      <p class="page-help-fallback">
        本页暂无专项说明，去
        <a href="javascript:;" @click="goManual">【操作手册】</a>
        搜索。
      </p>
    </template>
  </el-drawer>
</template>

<script setup>
import { computed, ref, watch, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { pageHelp } from '@/help/pageHelp'

const props = defineProps({
  /** false 时不挂 F1（窄屏 / 非 PC） */
  enabled: { type: Boolean, default: true }
})

const route = useRoute()
const router = useRouter()
const open = ref(false)
const tipsOpen = ref(false)

const help = computed(() => pageHelp[route.path] || null)
const drawerTitle = computed(() => (help.value ? help.value.title : '本页帮助'))

const sloganLines = computed(() => {
  const s = help.value?.slogan
  if (!s) return []
  return Array.isArray(s) ? s : [s]
})

/** 转义后给【按钮名】染蓝 */
function highlightOps(text) {
  if (!text) return ''
  return escapeHtml(text).replace(/【[^】]+】/g, (m) => `<span class="help-op">${m}</span>`)
}

/** 主句：【按钮】+「状态/路径」+ 补报/代报 染蓝 */
function highlightRich(text) {
  if (!text) return ''
  return escapeHtml(text)
    .replace(/【[^】]+】/g, (m) => `<span class="help-op">${m}</span>`)
    .replace(/「([^」]+)」/g, '<span class="help-tag">$1</span>')
    .replace(/补报/g, '<span class="help-tag">补报</span>')
    .replace(/代报/g, '<span class="help-tag">代报</span>')
}

/** 提醒段：【按钮】染蓝；「待复核」/"已通过" 等状态词染蓝（去掉引号） */
function highlightNote(text) {
  if (!text) return ''
  return escapeHtml(text)
    .replace(/【[^】]+】/g, (m) => `<span class="help-op">${m}</span>`)
    .replace(/「([^」]+)」/g, '<span class="help-tag">$1</span>')
    .replace(/"([^"]+)"/g, '<span class="help-tag">$1</span>')
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

const mustLines = computed(() =>
  (help.value?.items || []).filter((it) => !it.group || it.group === 'must')
)
const tipLines = computed(() =>
  (help.value?.items || []).filter((it) => it.group === 'tip')
)

watch(
  () => route.path,
  () => {
    tipsOpen.value = false
  }
)

function toggle() {
  open.value = !open.value
}

function goManual() {
  open.value = false
  router.push('/help')
}

function onKeydown(e) {
  if (e.key !== 'F1') return
  e.preventDefault()
  if (!props.enabled) return
  toggle()
}

function bindF1() {
  window.addEventListener('keydown', onKeydown)
}

function unbindF1() {
  window.removeEventListener('keydown', onKeydown)
}

watch(
  () => props.enabled,
  (on) => {
    unbindF1()
    if (on) bindF1()
    if (!on) open.value = false
  }
)

onMounted(() => {
  if (props.enabled) bindF1()
})

onUnmounted(() => {
  unbindF1()
})

defineExpose({ toggle, open })
</script>

<style scoped>
.page-help-value {
  margin: 0 0 16px;
  padding: 10px 12px;
  border-radius: 6px;
  border: 1px solid #cfe3ec;
  background: #f0f7fa;
  color: #1b6584;
  font-size: 14px;
  line-height: 1.55;
  font-weight: 500;
  white-space: pre-line;
}
.page-help-hero {
  margin: 0 0 16px;
  padding: 12px 14px;
  border-radius: 6px;
  border: 1px solid #d9ecff;
  background: #ecf5ff;
}
.page-help-hook {
  font-size: 16px;
  font-weight: 700;
  color: #303133;
  line-height: 1.5;
  margin-bottom: 6px;
}
.page-help-slogan {
  margin: 0;
  font-size: 13px;
  line-height: 1.6;
  color: #606266;
  font-style: italic;
}
.page-help-slogan.is-solution {
  color: #303133;
  font-style: normal;
  margin-top: 4px;
}
.page-help-list {
  margin: 0;
  padding: 0 0 0 18px;
  color: var(--app-text-1, #303133);
  font-size: 13px;
  line-height: 1.65;
}
.page-help-list li {
  margin-bottom: 10px;
}
:deep(.help-op) {
  color: #409eff;
  font-weight: 600;
  font-style: normal;
}
:deep(.help-tag) {
  color: #409eff;
  font-weight: 600;
  font-style: normal;
}
.help-note {
  color: #909399;
  font-size: 13px;
  margin: 8px 0 0;
  line-height: 1.6;
  white-space: pre-line;
}
.help-perm {
  color: #909399;
  font-size: 12px;
}
.page-help-tips {
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #dcdfe6;
}
.page-help-tips-toggle {
  display: block;
  width: 100%;
  margin: 0 0 8px;
  padding: 8px 10px;
  border: 1px solid #e4e7ed;
  border-radius: 6px;
  background: #fafafa;
  color: #606266;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
  line-height: 1.4;
}
.page-help-tips-toggle:hover {
  border-color: #c0c4cc;
  color: #303133;
  background: #f5f7fa;
}
.page-help-tips-list {
  margin-top: 4px;
}
.page-help-fallback {
  font-size: 14px;
  line-height: 1.6;
  color: var(--app-text-2, #606266);
}
.page-help-fallback a {
  color: var(--el-color-primary, #409eff);
  text-decoration: none;
}
.page-help-fallback a:hover {
  text-decoration: underline;
}
</style>
