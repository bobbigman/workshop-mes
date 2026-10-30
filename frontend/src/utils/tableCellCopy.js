// 复制显示文字，避免把状态值、操作按钮文字当作业务内容复制。
export function cellCopyText(cell, column, target) {
  if (!cell || ['selection', 'expand', 'index'].includes(column?.type)) return ''
  if (target?.closest?.('input, textarea, [contenteditable="true"]')) return ''
  const content = (cell.querySelector('.cell') || cell).cloneNode(true)
  content.querySelectorAll('button, input, textarea, select, .el-button, .el-checkbox, .el-radio, .el-switch, .el-icon, [role="button"]').forEach(node => node.remove())
  return (content.innerText ?? content.textContent ?? '').trim()
}

export async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }
  // 内网 HTTP 没有 Clipboard API，使用浏览器兼容方式。
  const active = document.activeElement
  const selection = window.getSelection()
  const ranges = Array.from({ length: selection?.rangeCount || 0 }, (_, i) => selection.getRangeAt(i).cloneRange())
  const input = document.createElement('textarea')
  input.value = text
  input.readOnly = true
  input.style.cssText = 'position:fixed;left:-9999px;top:0;'
  document.body.appendChild(input)
  try {
    input.select()
    if (!document.execCommand('copy')) throw new Error('浏览器未允许写入剪贴板')
  } finally {
    input.remove()
    active?.focus?.({ preventScroll: true })
    if (selection) {
      selection.removeAllRanges()
      ranges.forEach(range => selection.addRange(range))
    }
  }
}

let closeCurrentMenu = () => {}

export function openCellCopyMenu(event, text, notify) {
  closeCurrentMenu()
  const menu = document.createElement('button')
  menu.type = 'button'
  menu.textContent = '复制'
  menu.setAttribute('aria-label', '复制单元格内容')
  menu.style.cssText = 'position:fixed;z-index:10000;min-width:100px;padding:8px 20px;background:var(--el-bg-color-overlay,#fff);color:var(--el-text-color-primary,#303133);border:1px solid var(--el-border-color-light,#e4e7ed);border-radius:4px;box-shadow:var(--el-box-shadow-light,0 2px 12px #0002);cursor:pointer;font:inherit;text-align:left;'
  function close() {
    menu.remove()
    document.removeEventListener('pointerdown', outside, true)
    document.removeEventListener('keydown', keydown, true)
    window.removeEventListener('scroll', close, true)
    window.removeEventListener('resize', close)
    if (closeCurrentMenu === close) closeCurrentMenu = () => {}
  }
  function outside(e) { if (!menu.contains(e.target)) close() }
  function keydown(e) { if (e.key === 'Escape') close() }
  menu.onclick = async () => {
    close()
    try {
      await copyText(text)
      notify.success('已复制')
    } catch (error) {
      notify.error(`复制单元格内容失败：${error.message}。可选中文字后按 Ctrl+C 复制`)
    }
  }
  document.body.appendChild(menu)
  menu.style.left = `${Math.max(0, Math.min(event.clientX, window.innerWidth - menu.offsetWidth - 4))}px`
  menu.style.top = `${Math.max(0, Math.min(event.clientY, window.innerHeight - menu.offsetHeight - 4))}px`
  document.addEventListener('pointerdown', outside, true)
  document.addEventListener('keydown', keydown, true)
  window.addEventListener('scroll', close, true)
  window.addEventListener('resize', close)
  closeCurrentMenu = close
  return close
}
