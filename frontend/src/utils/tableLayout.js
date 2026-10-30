// 只存布局；表/列使用源码中的稳定标识，不使用运行时列序号。
export function tableLayoutKey(tableKey, factoryCode, userId) {
  return `mes.table-widths.v1:${[factoryCode || '', userId || '', tableKey].map(encodeURIComponent).join(':')}`
}

export function parseColumnWidths(raw) {
  if (!raw) return {}
  const value = JSON.parse(raw)
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('列宽记录格式无效')
  return Object.fromEntries(Object.entries(value).filter(([key, width]) =>
    key.length > 0 && key.length <= 1000 && Number.isInteger(width) && width >= 30 && width <= 10000))
}

export function readColumnWidths(storage, key, legacyKey) {
  const raw = storage.getItem(key)
  return parseColumnWidths(raw ?? (legacyKey ? storage.getItem(legacyKey) : null))
}

function propValue(props, camel, kebab = camel) {
  return props[camel] ?? props[kebab]
}

export function tableColumnKey(props, vnodeKey) {
  const explicit = propValue(props, 'columnKey', 'column-key')
  if (explicit != null && explicit !== '') return String(explicit)
  // 属性可能重复（如建议顺序和实际顺序），故加入标签；动态列优先显式 column-key。
  return JSON.stringify([props.prop || props.property || '', props.label || '', props.type || 'default', vnodeKey ?? ''])
}

export function tableColumnLayout(props, vnodeKey, widths, defaultTooltip = true) {
  const key = tableColumnKey(props, vnodeKey)
  const special = ['selection', 'expand', 'index'].includes(props.type)
  const operation = props.label === '操作'
  const resizable = !special && !operation && propValue(props, 'resizable') !== false
  const tooltip = propValue(props, 'showOverflowTooltip', 'show-overflow-tooltip')
  return {
    columnKey: key,
    // 始终绑定 width（弹性列用空串），重置时由原组件更新布局，无需重新挂载表格。
    width: (resizable && Object.hasOwn(widths, key) ? widths[key] : props.width) ?? '',
    resizable,
    showOverflowTooltip: special || operation ? false : tooltip ?? defaultTooltip
  }
}
