<template>
  <div class="mes-pager">
    <el-pagination
      :current-page="page"
      :page-size="pageSize"
      :total="total"
      :page-sizes="sizes"
      :background="background"
      layout="total, sizes, prev, pager, next, jumper"
      @update:current-page="onPage"
      @update:page-size="onSize"
    />
  </div>
</template>

<script setup>
/** 列表底栏统一分页：总数 + 每页条数 + 翻页 + 跳页（对标轻量 ERP，非金蝶重栏） */
defineProps({
  page: { type: Number, required: true },
  pageSize: { type: Number, required: true },
  total: { type: Number, default: 0 },
  sizes: { type: Array, default: () => [10, 20, 50, 100] },
  background: { type: Boolean, default: false }
})

const emit = defineEmits(['update:page', 'update:pageSize', 'change'])

function onPage(p) {
  emit('update:page', p)
  emit('change')
}

function onSize(s) {
  emit('update:pageSize', s)
  emit('update:page', 1)
  emit('change')
}
</script>

<style scoped>
.mes-pager {
  margin-top: 12px;
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
}
</style>
