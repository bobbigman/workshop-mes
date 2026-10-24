<template>
  <section v-if="productId && canUse(currentTier(),FEATURE.PieceWage)">
    <el-divider content-position="left">工序取价方式</el-divider>
    <p>启用后，新建工单按产品属性取价。未配置有效价目表时会提示缺价，已有工单保持原价。</p>
    <el-table :data="rows" size="small">
      <el-table-column prop="name" label="工序" />
      <el-table-column label="取价方式"><template #default="{row}"><el-select v-model="row.enabled" :disabled="busy" @change="v=>change(row,v)"><el-option :value="false" label="普通工价" /><el-option :value="true" label="产品属性价目表" /></el-select></template></el-table-column>
    </el-table>
  </section>
</template>
<script setup>
import { ref,watch } from 'vue'
import { getRouting } from '@/api/base'
import { getPricingModes,setPricingMode } from '@/api/operationPriceList'
import { canUse,currentTier,FEATURE } from '@/utils/licenseTier'
const props=defineProps({productId:Number,routingId:Number}),rows=ref([]),busy=ref(false)
watch(()=>[props.productId,props.routingId],async()=>{rows.value=[];if(!props.productId||!props.routingId||!canUse(currentTier(),FEATURE.PieceWage))return;const [r,m]=await Promise.all([getRouting(props.routingId),getPricingModes(props.productId)]);rows.value=r.data.steps.map(s=>({id:s.operationId,name:s.operationName,enabled:!!m.data.find(x=>x.operationId===s.operationId)?.attributePricing}))},{immediate:true})
async function change(row,value){busy.value=true;try{await setPricingMode(props.productId,row.id,value)}catch(e){row.enabled=!value;throw e}finally{busy.value=false}}
</script>
