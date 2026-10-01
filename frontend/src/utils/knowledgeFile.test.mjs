import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { getKnowledgeFileType } from './knowledgeFile.js'

test('识别扩展名、历史类型和 MIME，Excel 不误判成 Word', () => {
  for (const fileType of ['docx', '.DOCX', 'Word', 'application/octet-stream', '', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document']) {
    assert.equal(getKnowledgeFileType({ fileName: '打蛋器_作业指导书.DOCX', fileType }), 'docx')
  }
  assert.equal(getKnowledgeFileType({ fileType: 'application/pdf' }), 'pdf')
  assert.equal(getKnowledgeFileType({ fileName: '数量.xlsx', fileType: 'docx' }), 'xlsx')
  assert.equal(getKnowledgeFileType({ fileName: '无扩展名' }), '')
})

for (const path of ['../views/order/index.vue', '../components/KnowledgeFiles.vue', '../views/h5/report.vue']) {
  test(`${path}：Word/PDF 使用预览，只有 Excel 走下载`, async () => {
    const source = readFileSync(new URL(path, import.meta.url), 'utf8')
    const handler = source.slice(source.indexOf('async function openGuide(file)'), source.indexOf('\nfunction ', source.indexOf('async function openGuide(file)')))
    let downloads = 0
    const ref = () => ({ value: null })
    const context = vm.createContext({
      getKnowledgeFileType, getKnowledgeFileRaw: async () => ({ data: new Blob(['file']) }),
      docPreviewFile: ref(), docPreviewVisible: ref(), previewFile: ref(),
      imgPreviewUrls: ref(), imgPreviewVisible: ref(),
      URL: { createObjectURL: () => 'blob:test', revokeObjectURL() {} },
      document: { createElement: () => ({ click() { downloads++ }, remove() {} }), body: { appendChild() {} } },
      ElMessage: { error: text => assert.fail(text) }, showToast: text => assert.fail(text), console
    })
    vm.runInContext(handler, context)
    for (const extension of ['docx', 'pdf']) {
      await context.openGuide({ id: 1, fileName: `指导书.${extension}`, fileType: 'application/octet-stream' })
      const preview = context.docPreviewFile.value || context.previewFile.value
      assert.equal(preview.fileType, extension)
      assert.equal(downloads, 0)
    }
    await context.openGuide({ id: 2, fileName: '数量.xlsx', fileType: 'xlsx' })
    assert.equal(downloads, 1)
  })
}
