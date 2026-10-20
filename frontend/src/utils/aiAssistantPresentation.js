// 只改变PC展示，原始回答与后端引用校验保持完整。
const isSupportDoc = (source) => source.type === 'support_doc' || String(source.sourceId || '').startsWith('doc_')
export function visibleAiSources(sources = []) {
  return (sources || []).filter(source => !isSupportDoc(source))
}
export function presentAiAnswer(text = '', sources = []) {
  const hidden = new Set((sources || []).filter(isSupportDoc).map(source => source.sourceId))
  return text.replace(/\[?(doc_[\p{L}\p{N}_]+(?:#[\p{L}\p{N}_]+)?)\]?/gu,
    (full, id) => hidden.has(id) ? '' : full)
    .replace(/^[ \t]*(?:来源|参考来源|参考资料)[：:][ \t]*$/gm, '')
    .replace(/[ \t]+$/gm, '').trim()
}
