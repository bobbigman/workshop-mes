<template>
  <main class="h5-help-page">
    <header class="h5-help-header"><router-link to="/h5/home">‹ 返回工作台</router-link><h1>帮助</h1></header>
    <p class="help-note">操作上遇到问题？从这里找答案。</p>
    <van-cell-group inset>
      <van-cell title="AI操作帮助" label="询问怎么使用系统" is-link to="/h5/ai-help" />
      <van-cell title="操作手册" label="查看手机和PDA操作步骤" is-link :to="{ path: '/help', query: { topic: 'mobile/登录和找到自己的工单', from: 'h5' } }" />
      <van-cell title="常见问题" label="权限、数量和报工问题" is-link :to="{ path: '/help', query: { topic: 'faq/提示无报工权限', from: 'h5' } }" />
    </van-cell-group>
  </main>
</template>
<script setup>
import { Cell as VanCell, CellGroup as VanCellGroup } from 'vant'
import 'vant/lib/index.css'
import '@/styles/h5Help.css'
</script>
