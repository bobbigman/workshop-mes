<template>
  <div class="faq-page" v-loading="loading">
    <header class="faq-header">
      <h1>常见问题（系统能力50问）</h1>
      <p class="faq-desc">系统能不能做某事，按分组对照看。结论：✅ 支持 / ❌ 不支持 / ⚠️ 部分支持。</p>
    </header>

    <el-empty v-if="!loading && groups.length === 0" :description="emptyHint" />

    <section
      v-for="g in groups"
      :key="g.group"
      class="faq-group"
    >
      <h2 class="group-title">{{ g.group }}</h2>
      <article
        v-for="(q, i) in g.questions"
        :key="g.group + '-' + i"
        class="faq-item"
      >
        <h3 class="q-title">{{ q.title }}</h3>
        <p class="q-content">{{ q.content }}</p>
      </article>
    </section>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { getFaq } from '@/api/supportDocs'

const loading = ref(true)
const groups = ref([])
const emptyHint = '常见问题暂未收录'

onMounted(async () => {
  try {
    const res = await getFaq()
    groups.value = res?.code === 0 && Array.isArray(res.data) ? res.data : []
  } catch {
    groups.value = []
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.faq-page {
  max-width: 880px;
  margin: 0 auto;
  padding: 8px 8px 40px;
}
.faq-header {
  margin-bottom: 24px;
}
.faq-header h1 {
  margin: 0 0 8px;
  font-size: 22px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.faq-desc {
  margin: 0;
  font-size: 14px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}
.faq-group {
  margin-bottom: 28px;
}
.group-title {
  margin: 0 0 12px;
  padding-bottom: 8px;
  font-size: 17px;
  font-weight: 600;
  color: var(--el-color-primary);
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.faq-item {
  padding: 12px 0;
  border-bottom: 1px dashed var(--el-border-color-extra-light);
}
.faq-item:last-child {
  border-bottom: none;
}
.q-title {
  margin: 0 0 6px;
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  line-height: 1.5;
}
.q-content {
  margin: 0;
  font-size: 14px;
  color: var(--el-text-color-regular);
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
