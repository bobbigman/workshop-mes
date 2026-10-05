<template>
  <div class="manual-page">
    <header class="manual-header">
      <div><span class="eyebrow">微聚 · 帮助</span><h1>操作手册</h1></div>
      <router-link :to="returnPath" class="return-link">{{ signedIn ? '返回系统' : '返回登录' }}</router-link>
    </header>
    <main>
      <section class="intro" aria-label="手册搜索">
        <h2>要做什么，从这里找</h2>
        <p>第一次用，按步骤来；遇到问题，搜关键词。</p>
        <label class="search-label" for="manual-search">搜索操作步骤和常见问题</label>
        <div class="search-row">
          <el-input id="manual-search" v-model="query" clearable placeholder="例如：补报、改数、扫码、报工 复核" size="large" />
          <el-button v-if="query" size="large" @click="query = ''">清空搜索</el-button>
        </div>
        <p class="version">手册 {{ manualVersion }} · 更新 {{ manualUpdatedAt }} · 按同日系统界面编写</p>
      </section>

      <section v-if="query.trim()" class="search-results" aria-label="搜索结果">
        <h2 aria-live="polite">找到 {{ results.length }} 篇相关说明</h2>
        <p v-if="!results.length">没有找到。试试“报工”“权限”等短词，或清空搜索后按目录查找。</p>
        <button v-for="item in results" :key="item.id" class="result" @click="openChapter(item, true)">
          <span class="result-group">{{ groupTitle(item.group) }}</span>
          <strong>{{ item.title }}</strong>
          <span>{{ searchExcerpt(item, query) }}</span>
        </button>
      </section>

      <template v-else>
        <nav class="group-grid" aria-label="手册分类">
          <button v-for="(group, i) in groups" :key="group.id" :class="['group-card', { selected: selected.group === group.id }]"
            :aria-pressed="selected.group === group.id" @click="openGroup(group.id)">
            <span class="group-number">0{{ i + 1 }}</span>
            <strong>{{ group.title }}</strong><span>{{ group.description }}</span>
          </button>
        </nav>
        <p v-if="invalidTopic" role="status">这个章节地址已失效，已为你打开入门指南，请从目录重新选择。</p>
        <div class="reading-layout">
          <nav class="contents" :class="{ expanded: contentsOpen }" aria-label="章节目录">
            <div class="contents-heading">
              <h2>{{ groupTitle(selected.group) }}</h2>
              <button class="contents-toggle" :aria-expanded="contentsOpen" aria-controls="manual-contents" @click="contentsOpen = !contentsOpen">{{ contentsOpen ? '收起目录' : '展开目录' }}</button>
            </div>
            <div id="manual-contents" class="chapter-links">
              <router-link v-for="item in siblings" :key="item.id" :to="chapterLocation(item)"
              :aria-current="selected.id === item.id ? 'page' : undefined"
              :class="{ current: selected.id === item.id }">{{ item.title }}</router-link>
            </div>
          </nav>
          <article ref="article" class="article" tabindex="-1">
            <div class="article-label">{{ groupTitle(selected.group) }}</div>
            <h2>{{ selected.title }}</h2>
            <template v-for="(block, i) in selected.blocks" :key="selected.id + i">
              <figure v-if="block.type === 'image'">
                <a :href="block.src" target="_blank" rel="noopener" :aria-label="'放大查看：' + block.alt">
                  <img :src="block.src" :alt="block.alt" loading="lazy" />
                </a>
                <figcaption>{{ block.alt }}（点击图片可放大）</figcaption>
              </figure>
              <component :is="block.type" v-else-if="block.type === 'ol' || block.type === 'ul'">
                <li v-for="(parts, j) in block.items" :key="j"><template v-for="(part, k) in parts" :key="k"><strong v-if="part.bold">{{ part.text }}</strong><template v-else>{{ part.text }}</template></template></li>
              </component>
              <component :is="block.type" v-else><template v-for="(part, j) in block.parts" :key="j"><strong v-if="part.bold">{{ part.text }}</strong><template v-else>{{ part.text }}</template></template></component>
            </template>
            <footer class="article-footer">
              <span>需要发给同事？复制浏览器当前地址即可。</span>
              <router-link v-if="nextChapter" :to="chapterLocation(nextChapter)">下一篇：{{ nextChapter.title }} →</router-link>
            </footer>
          </article>
        </div>
      </template>
    </main>
    <footer class="manual-footer">
      <p class="credit-line">{{ creditLine }}</p>
      <p v-if="creditRoles" class="credit-roles">{{ creditRoles }}</p>
      <p>手册随系统提供 · 阅读不依赖 AI 或公网 · 实际操作权限以个人账号为准</p>
    </footer>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { chapters, groups, manualVersion, manualUpdatedAt } from '@/help/manual'
import { searchChapters, searchExcerpt } from '@/help/parser'
import { getInstanceInfo } from '@/api/auth'
import { creditLine, creditRoles } from '@/utils/instanceDisplay'

const route = useRoute()
const router = useRouter()
const article = ref(null)
const query = ref('')
const contentsOpen = ref(false)
const signedIn = !!localStorage.getItem('token')
const returnPath = signedIn ? (Number(localStorage.getItem('role')) === 2 ? '/h5/home' : '/order') : '/login'
const selected = computed(() => chapters.find(item => item.id === route.query.topic) || chapters[0])
const invalidTopic = computed(() => !!route.query.topic && !chapters.some(item => item.id === route.query.topic))
const siblings = computed(() => chapters.filter(item => item.group === selected.value.group))
const nextChapter = computed(() => siblings.value[siblings.value.findIndex(item => item.id === selected.value.id) + 1])
const results = computed(() => searchChapters(chapters, query.value))
const groupTitle = id => groups.find(group => group.id === id)?.title
const chapterLocation = item => ({ path: '/help', query: { topic: item.id } })

async function openChapter(item, clearSearch = false) {
  if (clearSearch) query.value = ''
  await router.push(chapterLocation(item))
  await focusArticle()
}

function openGroup(id) {
  return openChapter(chapters.find(item => item.group === id))
}

async function focusArticle() {
  await nextTick()
  article.value?.focus({ preventScroll: true })
  article.value?.scrollIntoView({ block: 'start' })
}

watch(() => route.query.topic, () => {
  query.value = ''
  contentsOpen.value = false
  focusArticle()
})

onMounted(async () => {
  try {
    await getInstanceInfo()
  } catch {
    // 回退胡工单方版
  }
})
</script>

<style scoped>
.manual-page { min-height: 100vh; background: var(--app-bg); color: var(--app-text-1); font-size: 16px; }
.manual-header { background: var(--app-card); border-bottom: 1px solid var(--app-border); padding: 22px max(24px, calc((100vw - 1200px) / 2)); display: flex; justify-content: space-between; align-items: center; gap: 20px; }
.eyebrow { font-size: 13px; color: var(--app-text-3); }
h1 { font-size: 26px; margin: 6px 0 0; }
.return-link { color: var(--app-primary); white-space: nowrap; text-decoration: none; }
main { max-width: 1200px; margin: auto; padding: 28px 24px; }
.intro h2 { font-size: 26px; margin: 0 0 8px; }
.intro > p { color: var(--app-text-2); margin: 8px 0 20px; }
.search-label { display: block; font-size: 14px; margin: 0 0 8px; }
.search-row { display: flex; max-width: 780px; gap: 10px; }
.intro .version { font-size: 12px; margin: 10px 0 24px; }
.group-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 24px; }
.group-card { text-align: left; padding: 18px; border: 1px solid var(--app-border); border-radius: var(--app-radius); background: var(--app-card); color: inherit; cursor: pointer; }
.group-card.selected { border-color: var(--app-primary); background: var(--app-primary-light); }
.group-number { color: var(--app-primary); font-weight: 700; font-size: 13px; }
.group-card strong { display: block; font-size: 17px; margin: 8px 0; }
.group-card > span:last-child { color: var(--app-text-2); font-size: 13px; line-height: 1.6; }
.reading-layout { display: grid; grid-template-columns: 250px minmax(0, 1fr); gap: 24px; align-items: start; }
.contents { background: var(--app-card); border: 1px solid var(--app-border); border-radius: var(--app-radius); padding: 18px 10px; }
.contents h2 { font-size: 15px; margin: 0 10px 12px; }
.contents-toggle { display: none; }
.contents a { display: block; padding: 11px 12px; border-radius: var(--app-radius-sm); color: var(--app-text-2); text-decoration: none; font-size: 14px; line-height: 1.5; }
.contents a.current { background: var(--app-primary-light); color: var(--app-primary); font-weight: 700; }
.article { background: var(--app-card); border: 1px solid var(--app-border); border-radius: var(--app-radius); padding: 30px; line-height: 1.9; overflow-wrap: anywhere; scroll-margin-top: 16px; }
.article-label { font-size: 13px; color: var(--app-primary); }
.article h2 { font-size: 26px; line-height: 1.4; margin: 8px 0 24px; }
.article p { margin: 16px 0; }
.article ol, .article ul { padding-left: 26px; }
.article li { margin: 12px 0; padding-left: 3px; }
.article figure { margin: 24px 0; }
.article img { max-width: 100%; max-height: 600px; object-fit: contain; border: 1px solid var(--app-border); border-radius: var(--app-radius-sm); box-sizing: border-box; }
.article figcaption { font-size: 13px; color: var(--app-text-3); }
.article-footer { border-top: 1px solid var(--app-border); margin-top: 30px; padding-top: 18px; font-size: 13px; display: flex; flex-direction: column; gap: 12px; color: var(--app-text-3); }
.article-footer a { color: var(--app-primary); }
.manual-footer { text-align: center; color: var(--app-text-3); padding: 16px 24px 30px; font-size: 12px; line-height: 1.6; }
.manual-footer .credit-line { margin: 0; font-size: 13px; }
.manual-footer .credit-roles { margin: 4px 0 8px; }
.manual-footer > p:last-child { margin: 0; }
.search-results h2 { font-size: 18px; }
.result { display: flex; flex-direction: column; gap: 8px; background: var(--app-card); border: 1px solid var(--app-border); border-radius: var(--app-radius); padding: 20px; text-align: left; width: 100%; margin: 12px 0; color: var(--app-text-2); line-height: 1.6; cursor: pointer; }
.result strong { color: var(--app-text-1); font-size: 18px; }
.result-group { color: var(--app-primary); font-size: 12px; }
button:hover, .contents a:hover { border-color: var(--app-primary); background: var(--app-primary-light); }
button:focus-visible, a:focus-visible { outline: 3px solid var(--app-primary); outline-offset: 3px; }
@media (max-width: 760px) {
  .manual-header { padding: 18px 16px; }
  main { padding: 22px 16px; }
  .group-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .group-card { padding: 14px; }
  .reading-layout { grid-template-columns: 1fr; gap: 16px; }
  .contents a { padding: 10px; }
  .contents-heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
  .contents h2 { margin-bottom: 0; }
  .contents-toggle { display: block; border: 0; background: transparent; color: var(--app-primary); font-size: 14px; padding: 8px; cursor: pointer; }
  .contents:not(.expanded) .chapter-links { display: none; }
  .contents.expanded .chapter-links { margin-top: 12px; }
  .article { padding: 20px; }
  .article h2 { font-size: 23px; }
  .search-row { flex-wrap: wrap; }
}
@media print {
  .manual-header, .intro, .group-grid, .contents, .manual-footer, .article-footer { display: none; }
  .reading-layout { display: block; }
  main, .article { padding: 0; border: 0; }
  .article img { max-height: 400px; }
}
</style>
