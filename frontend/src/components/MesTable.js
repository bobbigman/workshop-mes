import { cloneVNode, defineComponent, Fragment, h, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref } from 'vue'
import { ElMessage, ElTable } from 'element-plus'
import { readColumnWidths, tableColumnLayout, tableLayoutKey } from '../utils/tableLayout.js'
import { cellCopyText, openCellCopyMenu } from '../utils/tableCellCopy.js'

// 单根仍是原生 ElTable，保留 v-loading、页面样式、固定高度和所有业务事件。
export default defineComponent({
  name: 'MesTable',
  inheritAttrs: false,
  props: {
    tableKey: { type: String, required: true },
    legacyOrderWidths: Boolean
  },
  setup(props, { attrs, slots, expose }) {
    const table = ref(null)
    const widths = ref({})
    /** 列表页 flex 定高后同步给 ElTable，表体内部滚，避免整表 overflow 弄坏固定列 */
    const boundHeight = ref(undefined)
    let storageKey = '', legacyKey = ''
    let closeCopyMenu = () => {}
    let resizeObserver = null
    onBeforeUnmount(() => {
      closeCopyMenu()
      resizeObserver?.disconnect()
      resizeObserver = null
    })
    onDeactivated(() => closeCopyMenu())

    function syncBoundHeight() {
      if (attrs.height != null && attrs.height !== '') return
      const el = table.value?.$el
      if (!el || typeof el.getBoundingClientRect !== 'function') return
      const h = Math.floor(el.getBoundingClientRect().height)
      if (h >= 80) boundHeight.value = h
    }

    onMounted(async () => {
      await nextTick()
      const el = table.value?.$el
      if (!el) return
      syncBoundHeight()
      resizeObserver = new ResizeObserver(() => syncBoundHeight())
      resizeObserver.observe(el)
      if (el.parentElement) resizeObserver.observe(el.parentElement)
    })
    onActivated(async () => {
      await nextTick()
      syncBoundHeight()
    })

    function copyMenu(row, column, cell, event) {
      closeCopyMenu()
      const text = cellCopyText(cell, column, event.target)
      if (!text || event.defaultPrevented) return
      event.preventDefault()
      closeCopyMenu = openCellCopyMenu(event, text, ElMessage)
    }

    function loadWidths() {
      try {
        const factory = localStorage.getItem('factoryCode') || ''
        const user = localStorage.getItem('userId') || ''
        const nextKey = tableLayoutKey(props.tableKey, factory, user)
        if (storageKey === nextKey) return
        storageKey = nextKey
        legacyKey = props.legacyOrderWidths
          ? `mes.order.column-widths.v1:${encodeURIComponent(factory)}:${encodeURIComponent(user)}` : ''
        widths.value = readColumnWidths(localStorage, storageKey, legacyKey)
      } catch (error) {
        widths.value = {}
        ElMessage.warning(`读取表格列宽失败，已使用默认列宽：${error.message}`)
      }
    }
    loadWidths()
    onActivated(loadWidths)

    function saveWidth(width, oldWidth, column) {
      if (!column.resizable || !column.columnKey || !Number.isFinite(width)) return
      const value = Math.round(width)
      if (value < 30 || value > 10000) return
      widths.value = { ...widths.value, [column.columnKey]: value }
      try {
        if (!storageKey) throw new Error('无法识别当前账号的表格设置')
        localStorage.setItem(storageKey, JSON.stringify(widths.value))
      } catch (error) {
        ElMessage.error(`保存表格列宽失败，下次打开无法恢复：${error.message}`)
      }
    }

    function resetWidths() {
      try {
        if (!storageKey) throw new Error('无法识别当前账号的表格设置')
        // 先清旧记录，避免新记录删除后又恢复旧布局。
        if (legacyKey) localStorage.removeItem(legacyKey)
        localStorage.removeItem(storageKey)
      } catch (error) {
        ElMessage.error(`清除表格列宽记录失败：${error.message}`)
        return
      }
      widths.value = {}
      ElMessage.success('已恢复默认列宽')
    }

    function mapColumns(nodes) {
      return nodes.map(node => {
        if (node.type === Fragment && Array.isArray(node.children)) {
          const copy = cloneVNode(node)
          copy.children = mapColumns(node.children)
          // 不沿用编译器块缓存，否则动态列可能绕过新增的宽度绑定。
          copy.dynamicChildren = null
          return copy
        }
        if (node.type?.name !== 'ElTableColumn') return node
        const layout = tableColumnLayout(node.props || {}, node.key, widths.value,
          attrs.showOverflowTooltip ?? attrs['show-overflow-tooltip'] ?? true)
        const copy = cloneVNode(node, layout)
        copy.dynamicChildren = null
        return copy
      })
    }

    // 透传公开表格实例方法，调用方继续可用 clearSelection / doLayout 等。
    expose(new Proxy({}, {
      get: (_, key) => {
        const value = table.value?.[key]
        return typeof value === 'function' ? value.bind(table.value) : value
      },
      has: (_, key) => key in (table.value || {})
    }))

    return () => h(ElTable, {
      ...attrs,
      ref: table,
      class: [attrs.class, 'mes-table'],
      border: true,
      height: attrs.height ?? boundHeight.value,
      showOverflowTooltip: attrs.showOverflowTooltip ?? attrs['show-overflow-tooltip'] ?? true,
      onHeaderDragend: [saveWidth, attrs.onHeaderDragend].filter(Boolean),
      onCellContextmenu: [attrs.onCellContextmenu, copyMenu].filter(Boolean)
    }, {
      ...slots,
      default: () => mapColumns(slots.default?.() || []),
      append: () => [
        slots.append?.(),
        h('div', { class: 'mes-table-layout-tools' }, [
          h('button', {
            type: 'button',
            class: 'mes-table-reset',
            title: '仅恢复当前表格的默认列宽',
            onClick: resetWidths
          }, '恢复默认列宽')
        ])
      ]
    })
  }
})
