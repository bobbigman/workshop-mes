<template>
  <div class="about-page">
    <div class="about-dialog" role="dialog" aria-modal="true" aria-label="关于">
      <div class="dlg-titlebar">
        <span>关于 {{ PRODUCT_TITLE }}</span>
      </div>

      <div class="dlg-body">
        <!-- 品牌区：仿 WPS 的居中 Logo + 产品名 -->
        <div class="brand">
          <img class="brand-mark" src="/brand/xiaomifeng-mark.svg" alt="小蜜蜂" />
          <div class="brand-name">{{ PRODUCT_TITLE }}</div>
          <div class="brand-sub">小蜜蜂 · 轻MES</div>
          <div class="brand-ver">{{ PRODUCT_TITLE }}({{ version || '—' }})</div>
        </div>

        <!-- 版本 / 版权 / 厂名元信息 -->
        <dl class="meta">
          <div class="row">
            <dt>本机厂名</dt>
            <dd>{{ factoryLabel || instanceLabel || '—' }}</dd>
          </div>
          <div class="row">
            <dt>版权</dt>
            <dd>
              <div>{{ creditLine }}</div>
              <div class="rights">{{ CREDIT_RIGHTS }}</div>
              <div v-if="creditRoles" class="roles">{{ creditRoles }}</div>
            </dd>
          </div>
        </dl>

        <!-- 产品使用权（仿 WPS 输入框行） -->
        <div class="owner">
          <span class="owner-label">本产品使用权属于</span>
          <span class="owner-value">{{ ownerLabel }}</span>
        </div>

        <!-- 免责警告（仿 WPS 底部警告条） -->
        <div class="warn">
          <span class="warn-icon" aria-hidden="true">⚠</span>
          <span>
            本软件按现有功能提供，不作特定生产结果或零故障承诺。因使用或无法使用本软件造成的间接损失、停产损失、预期利润等，开发方与联合出品方不承担责任；在法律允许范围内，若需承担赔偿责任，累计赔偿不超过该客户就本软件<strong>已实际支付的费用</strong>，书面合同另有约定的，以合同为准。
          </span>
        </div>

        <!-- 著作权警告（仿 WPS 底部警告条） -->
        <div class="warn warn-copyright">
          <span class="warn-icon" aria-hidden="true">⚠</span>
          <span>本计算机程序受《中华人民共和国著作权法》和《计算机软件保护条例》的保护，未经授权擅自复制或传播本程序的部分或全部，将依法追究法律责任。</span>
        </div>
      </div>

      <!-- 底部：左链接 · 右确认按钮 -->
      <div class="dlg-footer">
        <div class="dlg-links">
          <router-link to="/help">操作手册</router-link>
          <span class="sep">·</span>
          <router-link to="/advantages">产品优势速览</router-link>
          <span class="sep">·</span>
          <a href="javascript:;" class="terms-link" @click="showTerms = true">使用条款</a>
        </div>
        <el-button type="primary" size="small" @click="goBack">确认</el-button>
      </div>
    </div>

    <!-- 使用条款轻弹层（仿 WPS 许可协议） -->
    <el-dialog v-model="showTerms" title="使用条款" width="520px" append-to-body>
      <ol class="terms-list">
        <li><strong>授权范围</strong>：本软件供授权企业局域网部署使用，仅限约定范围内使用；未经授权不得复制、传播或用于约定用途之外。</li>
        <li><strong>著作权</strong>：本软件受《中华人民共和国著作权法》和《计算机软件保护条例》的保护，著作权归联合出品方；未经授权擅自复制或传播本程序的部分或全部，依法追究法律责任。</li>
        <li><strong>免责声明</strong>：本软件按现有功能提供，用于辅助车间报工与进度管理，不作特定生产结果或零故障承诺；因使用或无法使用本软件造成的间接损失、停产损失、预期利润等，开发方与联合出品方不承担责任。</li>
        <li><strong>赔偿上限</strong>：在法律允许范围内，若需承担赔偿责任，累计赔偿不超过该客户就本软件已实际支付的费用；书面合同另有约定的，以合同为准。</li>
        <li><strong>技术支持</strong>：本软件供局域网部署使用，技术支持请联系产品研发或客户成功经理。</li>
      </ol>
      <template #footer>
        <el-button type="primary" @click="showTerms = false">知道了</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getInstanceInfo } from '@/api/auth'
import { ElMessage } from 'element-plus'
import {
  PRODUCT_TITLE,
  CREDIT_RIGHTS,
  creditLine,
  creditRoles,
  resolveFactoryLabel,
  resolveInstanceLabel
} from '@/utils/instanceDisplay'

const router = useRouter()

const showTerms = ref(false)
const version = ref(typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '')
const displayName = ref(localStorage.getItem('instanceName') || '')
const factoryName = ref(localStorage.getItem('factoryName') || '')

const factoryLabel = computed(() => factoryName.value || resolveFactoryLabel(displayName.value))
const instanceLabel = computed(() => resolveInstanceLabel(displayName.value))
const ownerLabel = computed(() => factoryLabel.value || instanceLabel.value || PRODUCT_TITLE)

onMounted(async () => {
  try {
    const res = await getInstanceInfo()
    if (res.data?.displayName) displayName.value = res.data.displayName
    if (res.data?.version) version.value = res.data.version
  } catch {
    ElMessage.warning('实例信息加载失败，部分内容可能不是最新（版本仍显示构建号）')
  }
})

function goBack() {
  if (window.history.length > 1) {
    router.back()
  } else {
    router.push('/')
  }
}
</script>

<style scoped>
.about-page {
  min-height: 100%;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 40px 16px;
  box-sizing: border-box;
  background: var(--app-bg);
}

/* —— 仿 WPS「关于」弹窗 —— */
.about-dialog {
  width: 620px;
  max-width: 100%;
  background: var(--app-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius);
  box-shadow: var(--app-shadow-lg);
  overflow: hidden;
  box-sizing: border-box;
}

.dlg-titlebar {
  padding: 14px 24px;
  border-bottom: 1px solid var(--app-border-light);
  text-align: center;
}

.dlg-titlebar span {
  font-size: 14px;
  font-weight: 500;
  color: var(--app-text-2);
}

.dlg-body {
  padding: 28px 40px 20px;
}

/* 品牌区：居中 */
.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  margin-bottom: 22px;
}

.brand-mark {
  width: 72px;
  height: 72px;
  margin-bottom: 14px;
  display: block;
}

.brand-name {
  font-size: 24px;
  font-weight: 600;
  color: var(--app-text-1);
  letter-spacing: 1px;
}

.brand-sub {
  margin-top: 4px;
  font-size: 13px;
  color: var(--app-text-2);
  letter-spacing: 1px;
}

.brand-ver {
  margin-top: 6px;
  font-size: 12px;
  color: var(--app-text-3);
}

/* 元信息 */
.meta {
  margin: 0;
  border-top: 1px solid var(--app-border-light);
  border-bottom: 1px solid var(--app-border-light);
  padding: 6px 0;
}

.row {
  display: grid;
  grid-template-columns: 110px 1fr;
  gap: 12px;
  padding: 10px 0;
  align-items: start;
}

.row + .row {
  border-top: 1px dashed var(--app-border-light);
}

dt {
  margin: 0;
  font-size: 13px;
  color: var(--app-text-3);
  line-height: 1.6;
}

dd {
  margin: 0;
  font-size: 13px;
  color: var(--app-text-1);
  line-height: 1.6;
}

.rights {
  margin-top: 2px;
  font-size: 12px;
  color: var(--app-text-3);
}

.roles {
  margin-top: 4px;
  font-size: 12px;
  color: var(--app-text-2);
}

/* 产品使用权行 */
.owner {
  margin-top: 18px;
  display: flex;
  align-items: center;
  gap: 10px;
}

.owner-label {
  font-size: 13px;
  color: var(--app-text-2);
}

.owner-value {
  min-height: 32px;
  flex: 1;
  max-width: 300px;
  padding: 0 12px;
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
  background: var(--app-bg);
  display: flex;
  align-items: center;
  font-size: 13px;
  color: var(--app-text-1);
}

/* 免责警告条 */
.warn {
  margin-top: 20px;
  display: flex;
  gap: 8px;
  align-items: flex-start;
  font-size: 12px;
  line-height: 1.7;
  color: var(--app-text-3);
}

.warn-icon {
  flex-shrink: 0;
  line-height: 1.7;
}

.warn-copyright {
  margin-top: 10px;
}

/* 使用条款弹层 */
.terms-list {
  margin: 0;
  padding-left: 22px;
  font-size: 14px;
  line-height: 1.85;
  color: var(--app-text-2);
}

.terms-list li {
  margin: 10px 0;
}

.terms-list strong {
  color: var(--app-text-1);
}

/* 底部 */
.dlg-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 24px;
  border-top: 1px solid var(--app-border-light);
}

.dlg-links {
  font-size: 13px;
}

.dlg-links a {
  color: var(--app-primary);
  text-decoration: none;
}

.dlg-links a:hover {
  text-decoration: underline;
}

.dlg-links .sep {
  margin: 0 8px;
  color: var(--app-text-4);
}

@media (max-width: 520px) {
  .about-page { padding: 20px 12px; }
  .dlg-body { padding: 24px 22px 16px; }
  .row { grid-template-columns: 1fr; gap: 4px; }
  .dlg-footer { flex-direction: column; gap: 12px; align-items: stretch; }
  .dlg-footer .el-button { width: 100%; }
}
</style>
