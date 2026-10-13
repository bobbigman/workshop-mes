# 69-执行指令-内嵌文档预览（PDF + Word，手机端 + PC 端，作业指导书/图纸）

> 状态：已执行（2026-09-25）。执行前修正了 3 处技术问题：worker 加载方式、mammoth 导入/返回值、弹层载体；详见文中「> 修正」标注。
> 分工：本指令只做分析与方案，代码由 Cursor 编写执行。
> 目标文件：
> - `frontend/src/views/h5/report.vue`（手机端 H5 报工页）
> - `frontend/src/components/KnowledgeFiles.vue`（PC 端通用组件）

---

## 一、背景

作业指导书/图纸列表点开文件，现状：

- **PDF**：两个端都用 `window.open(blobUrl, '_blank')` 新窗口渲染。
  - **手机端**（`report.vue`）：移动端浏览器（尤其 iOS Safari、部分安卓）**不支持用 `blob:` 地址新开窗口渲染 PDF**，且 `window.open` 在异步拿到 blob 之后调用，已脱离点击手势上下文，**容易被拦截** → 表现为**点 PDF 无反应**。
  - **PC 端**（`KnowledgeFiles.vue`）：桌面 Chrome 能弹出来所以"看得到"，但仍是新窗口、体验不统一，同样有隐患。
- **docx（Word）**：两个端都走「创建 `<a download>` 触发下载」→ 手机端一点就下载。**浏览器不原生预览 Word**，用户希望手机端也能**在页面里直接看**而不是下载。
- 图片（jpg/png/webp）：手机 vant `showImagePreview` / PC `el-image-viewer`，正常。

**结论**：不是后端问题，是前端把 PDF/docx 都做成了"新窗口/下载"，体验差。改为两端统一的内嵌预览（PDF 用 pdf.js、docx 用 mammoth.js，均纯前端、离线可用）。

## 二、目标

手机端/PC端点开作业指导书、图纸：

- **PDF** → 页面内弹层直接渲染，可翻页、缩放，不跳新窗口；
- **docx（Word）** → 页面内弹层渲染成可读内容（文字/表格/图片），不再触发下载；
- 均不依赖外部网络（内网部署可用）。

## 三、方案（pdf.js 内嵌预览）

### 1. 依赖

- 安装 `pdfjs-dist`（用最新稳定版）。
- 内网部署**不能依赖 CDN**，worker 必须随前端打包本地化：
  ```js
  import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl
  ```
  （Vite 用 `?url` 把 worker 作为静态资源打包，离线可用。）

  > 修正：原稿用 `new URL('pdfjs-dist/build/pdf.worker.min.mjs', import.meta.url)`，但 Vite 对 node_modules 内的包路径不会改写该 `new URL`，运行时解析会指向错误位置；须改用 `?url` 静态导入。

### 2. 新增 PDF/Word 预览弹层（全屏遮罩层）

在 `report.vue` 模板中新增一个全屏遮罩层（`position: fixed`）承载预览，顶部标题 + 关闭按钮，内容区放 `DocPreview` 组件：

- `<canvas>` 渲染当前页（PDF）；`v-html` 渲染 Word 内容；
- 控件：上一页 / 下一页、页码/总页显示、缩放（+ / − 或百分比）；
- 打开/关闭状态、加载中/失败提示。

> 修正：原稿写「vant Popup」。项目 vant 按需引入、不全局注册，额外引 `Popup` 组件不划算；全屏遮罩层更简单、阅读 PDF 面积更大，故改用遮罩层。

### 3. 渲染逻辑

- 打开时：用已拿到的 blob → `arrayBuffer()` → `pdfjsLib.getDocument({ data })` → 渲染第 1 页。
- 切页 / 缩放：重算 viewport（`page.getViewport({ scale })`）并 `page.render` 到 canvas。
- **移动端适配**：canvas 宽度撑满弹层宽度，`scale` 按容器宽度与页面宽度的比值计算。
- **关闭时**：`pdf.destroy()` 释放实例，避免内存泄漏与重复加载。
- **失败兜底**：渲染异常给文字提示（如「PDF 打开失败」），不能白屏。

### 4. 修改 openGuide 分支（手机端 report.vue）

```js
if (图片) { showImagePreview(url) }                // 保持不变
else if (pdf) { 打开 PDF 预览弹层 }                // 改为内嵌预览，不再 window.open
else if (docx) { 打开 Word 预览弹层 }              // 改为内嵌预览，不再下载
else { <a download> 触发下载 }                      // xlsx 等暂保持下载
```

### 5. docx 预览方案（mammoth.js）

浏览器不原生预览 Word，用 **mammoth.js** 纯前端把 docx 解析成 HTML 显示。

1. 依赖：`npm i mammoth`（打包进 bundle，离线可用）。
2. 导入：`import * as mammoth from 'mammoth'` 即可——包自带 `browser` 字段把 `lib/unzip.js`、`lib/docx/files.js` 的 Node 实现映射成浏览器版，Vite 打包即浏览器可用，**无需另引 `mammoth.browser.js`**。
3. 渲染：打开时 blob → `arrayBuffer()` → `mammoth.convertToHtml({ arrayBuffer }, { convertImage: mammoth.images.dataUri })`；返回的是 `Result` 对象，**HTML 在 `result.value`**（不是直接字符串），再 `v-html` 放进弹层。
4. 图片：不传 `convertImage` 时内嵌图片会被丢弃；传 `mammoth.images.dataUri` 把图片内联成 `data:` URL（单张大小靠 20MB 总上限兜底，首版不做单张裁剪）。
5. 弹层：复用 PDF 预览弹层容器，内容区可滚动；移动端宽度撑满。
6. 关闭时：清空 `v-html` 内容，组件卸载时统一清理。
7. 失败兜底：`convertToHtml` 异常给提示（如「Word 打开失败」），不白屏。
8. 局限说明：mammoth 还原的是文字/标题/列表/表格/图片，不能 100% 还原复杂 Word 版式；作业指导书（文字步骤类）够用。

### 6. PC 端同步修改（KnowledgeFiles.vue）

同一份逻辑，改为页面内嵌预览：

- `openGuide` 的 pdf 分支不再 `window.open(url, '_blank')`，改为打开 PDF 预览弹层（`el-dialog` 承载）；
- `openGuide` 的 docx 分支不再 `<a download>` 下载，改为打开 Word 预览弹层（mammoth 渲染，`el-dialog` 承载）；
- xlsx 等保持 `<a download>` 下载；图片保持 `el-image-viewer` 预览。

### 7. 抽一个共用 DocPreview 组件（已采用）

为两端复用同一套渲染逻辑，抽公共组件 `frontend/src/components/DocPreview.vue`：

- 接收 `blob` + `fileType`，内部按类型分流：
  - pdf → pdf.js 渲染 canvas + 翻页/缩放 + 关闭销毁；
  - docx → mammoth 渲染 HTML + 关闭清理；
- 手机端用全屏遮罩层包它、PC 端用 `el-dialog` 包它，渲染内核只写一份；
- 关闭/卸载时 `pdf.destroy()`、`renderTask.cancel()`，防止内存泄漏。

## 四、关键注意

1. 内网部署，worker（pdf.js）与 mammoth 都随前端打包，禁止引 CDN。
2. blob 已由 `getKnowledgeFileRaw`（带 Authorization header）取得，直接喂给 pdf.js / mammoth，无需额外 URL、无需改后端。
3. PDF：渲染完 / 关闭弹层务必 `pdf.destroy()`；docx：关闭清空 `v-html`。
4. 加载中给 loading，失败给提示，不白屏、不静默失败。
5. 版本兼容：`pdfjs-dist` 已装 6.x（最新稳定，ESM-only，需现代浏览器约 Chrome 119+/Safari 17.4+）。若客户手机/国产浏览器过旧导致 PDF 打开失败，回退方案是 pin `pdfjs-dist@3.11.x`（legacy 构建、worker 为 `.js`）；当前已兜底提示「PDF 打开失败」，不会白屏。

## 五、验收清单

**手机端（report.vue）**
- [ ] 点开 PDF → 弹层内显示第 1 页，不跳新窗口；
- [ ] PDF 翻页、缩放正常，页码/总页正确；
- [ ] 点开 docx → 弹层内显示文字/表格/图片，不再触发下载；
- [ ] docx 内嵌图片能显示（convertImage 生效）；
- [ ] xlsx 仍走下载，图片仍走图片预览；
- [ ] 内网（断外网）环境可正常渲染（worker/mammoth 已本地化）；
- [ ] 关闭后再点开其它文件正常，无残留、无内存泄漏；
- [ ] 文件异常/网络失败时有提示，不白屏。

**PC 端（KnowledgeFiles.vue）**
- [ ] 点开 PDF → 弹窗内预览，不再新开标签页；
- [ ] 点开 docx → 弹窗内预览，不再下载；
- [ ] PDF 翻页/缩放、docx 文字图片正常；xlsx 下载、图片预览不变；
- [ ] 断外网可渲染；关闭销毁无残留。

## 六、交付要求

改完 `npm run build` 分别验证手机端 H5 与 PC 端，给出「通过 / 需调整」结论；若遇 pdf.js / mammoth 版本或 Vite 打包问题，记录解决办法。
