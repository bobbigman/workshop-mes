<template>
  <div class="dp">
    <div v-if="loading" class="dp-state">加载中…</div>
    <div v-else-if="error" class="dp-state dp-error">{{ error }}</div>

    <template v-else-if="fileType === 'pdf'">
      <div class="dp-toolbar">
        <button type="button" class="dp-btn" :disabled="page <= 1" @click="goPage(-1)">上一页</button>
        <span class="dp-page">{{ page }} / {{ pageCount }}</span>
        <button type="button" class="dp-btn" :disabled="page >= pageCount" @click="goPage(1)">下一页</button>
        <span class="dp-spacer"></span>
        <button type="button" class="dp-btn" :disabled="zoom <= 0.5" @click="zoomBy(-1)">−</button>
        <span class="dp-page">{{ Math.round(zoom * 100) }}%</span>
        <button type="button" class="dp-btn" :disabled="zoom >= 4" @click="zoomBy(1)">＋</button>
      </div>
      <div ref="pdfScroller" class="dp-scroll">
        <canvas ref="pdfCanvas" class="dp-canvas"></canvas>
      </div>
    </template>

    <div v-else class="dp-scroll dp-docx" v-html="docxHtml"></div>
  </div>
</template>

<script setup>
// 【业务背景】作业指导书/图纸内嵌预览（docs/69）：PDF 用 pdf.js、Word 用 mammoth，均纯前端、离线可用。
import { ref, shallowRef, watch, nextTick, onBeforeUnmount } from 'vue'
// 预览依赖只能在打开对应文件时加载。旧手机无法解析 PDF 库时，
// 错误应留在预览弹层内，不能阻断工单路由或连带影响 Word 预览。

const props = defineProps({
  blob: { type: [Blob, null], default: null },
  fileType: { type: String, default: '' }, // pdf / docx
  fileName: { type: String, default: '' }
})

const loading = ref(false)
const error = ref('')
// pdf 状态
const pdfDoc = shallowRef(null)
const page = ref(1)
const pageCount = ref(0)
const zoom = ref(1)
const pdfCanvas = ref(null)
const pdfScroller = ref(null)
// docx 状态
const docxHtml = ref('')

let renderTask = null
let pdfLoadingTask = null
let renderSeq = 0

watch(() => props.blob, (b) => load(b), { immediate: true })

async function load(b) {
  reset()
  if (!b) return
  loading.value = true
  error.value = ''
  try {
    const buf = await b.arrayBuffer()
    if (props.fileType === 'pdf') await loadPdf(buf)
    else if (props.fileType === 'docx') await loadDocx(buf)
  } catch (e) {
    error.value = (props.fileType === 'pdf' ? 'PDF' : 'Word') + ' 打开失败'
    console.error('[DocPreview] 打开失败', e)
  } finally {
    loading.value = false
  }
}

async function loadPdf(buf) {
  await import('@/utils/pdfCompatibility')
  const [pdfjsLib, { default: pdfWorkerUrl }] = await Promise.all([
    import('pdfjs-dist/legacy/build/pdf.mjs'),
    import('@/workers/pdf.worker.js?worker&url')
  ])
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl
  pdfLoadingTask = pdfjsLib.getDocument({ data: buf })
  pdfDoc.value = await pdfLoadingTask.promise
  pageCount.value = pdfDoc.value.numPages || 0
  page.value = 1
  zoom.value = 1
  // 先显示 canvas，再绘制首页；loading 分支中 canvas 尚未挂载。
  loading.value = false
  await nextTick()
  await renderPage()
}

async function renderPage() {
  if (!pdfDoc.value || !pdfCanvas.value) return
  const seq = ++renderSeq
  const p = await pdfDoc.value.getPage(page.value)
  if (seq !== renderSeq || !pdfDoc.value) return // 已被翻页/关闭覆盖，丢弃
  const scroller = pdfScroller.value || pdfCanvas.value.parentElement
  const containerWidth = scroller ? scroller.clientWidth : 0
  const base = p.getViewport({ scale: 1 })
  const cssWidth = containerWidth > 0 ? containerWidth : base.width
  // 高分屏：按 devicePixelRatio 提高实际渲染分辨率，CSS 再缩回，文字更清晰
  const dpr = window.devicePixelRatio || 1
  const viewport = p.getViewport({ scale: (cssWidth / base.width) * zoom.value * dpr })
  const canvas = pdfCanvas.value
  canvas.width = Math.floor(viewport.width)
  canvas.height = Math.floor(viewport.height)
  canvas.style.width = Math.floor(viewport.width / dpr) + 'px'
  canvas.style.height = Math.floor(viewport.height / dpr) + 'px'
  if (renderTask) { try { renderTask.cancel() } catch { /* noop */ } }
  renderTask = p.render({ canvasContext: canvas.getContext('2d'), viewport })
  try {
    await renderTask.promise
  } catch (e) {
    if (e && e.name === 'RenderingCancelledException') return
    throw e
  }
}

function goPage(delta) {
  const next = page.value + delta
  if (next < 1 || next > pageCount.value) return
  page.value = next
  renderPage()
}

function zoomBy(delta) {
  const next = zoom.value * (delta > 0 ? 1.2 : 1 / 1.2)
  zoom.value = Math.min(4, Math.max(0.5, next))
  renderPage()
}

async function loadDocx(buf) {
  const module = await import('mammoth')
  const mammoth = module.default || module
  const result = await mammoth.convertToHtml({ arrayBuffer: buf }, {
    convertImage: mammoth.images.dataUri // 内嵌图片转 data: URL，否则图片被丢弃
  })
  docxHtml.value = result.value || ''
}

function reset() {
  renderSeq++
  if (renderTask) { try { renderTask.cancel() } catch { /* noop */ } renderTask = null }
  if (pdfLoadingTask) {
    pdfLoadingTask.destroy().catch(e => console.error('[DocPreview] 释放 PDF 失败', e))
    pdfLoadingTask = null
  }
  pdfDoc.value = null
  page.value = 1
  pageCount.value = 0
  zoom.value = 1
  docxHtml.value = ''
  error.value = ''
}

onBeforeUnmount(reset)
</script>

<style scoped>
.dp { display: flex; flex-direction: column; height: 100%; min-height: 0; flex: 1; }
.dp-state { padding: 40px 16px; text-align: center; color: var(--app-text-3, #909399); font-size: 14px; }
.dp-error { color: var(--app-warning, #e6a23c); }
.dp-toolbar {
  display: flex; align-items: center; gap: 8px; padding: 8px 12px; flex: none;
  border-bottom: 1px solid var(--app-border, #e5e6eb); background: var(--app-card, #fff);
}
.dp-btn {
  min-width: 52px; height: 32px; padding: 0 10px; border: 1px solid var(--app-border, #dcdfe6);
  border-radius: 6px; background: var(--app-card, #fff); color: var(--app-text-1, #303133); font-size: 14px;
}
.dp-btn:disabled { opacity: .4; }
.dp-page { font-size: 13px; color: var(--app-text-2, #606266); white-space: nowrap; }
.dp-spacer { flex: 1; }
.dp-scroll { flex: 1; min-height: 0; overflow: auto; -webkit-overflow-scrolling: touch; }
.dp-canvas { display: block; margin: 0 auto; }
.dp-docx { padding: 16px; line-height: 1.7; font-size: 15px; color: var(--app-text-1, #303133); word-break: break-word; }
.dp-docx :deep(img) { max-width: 100%; height: auto; }
.dp-docx :deep(table) { border-collapse: collapse; max-width: 100%; }
.dp-docx :deep(td), .dp-docx :deep(th) { border: 1px solid var(--app-border, #e5e6eb); padding: 4px 8px; }
</style>
