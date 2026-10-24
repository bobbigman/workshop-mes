<template>
  <section v-if="loading || error || rows.length" class="order-processing">
    <h3>本单工序工价</h3>
    <p>按产品规格自动取价，保存时锁定；已有报工后不可更改。</p>
    <p v-if="loading">正在加载加工要求…</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-button v-if="error" @click="load">重试</el-button>
    <el-card v-for="row in rows" :key="row.id" shadow="never" class="processing-task">
      <h4>{{ row.name }}</h4>
      <p v-if="row.legacy">此历史工序已有报工，继续使用原工价表；不可中途切换加工计价。</p>
      <template v-if="row.keep">
        <el-tag>本单已锁价</el-tag> <el-button v-if="row.config && !row.automatic" link type="primary" :disabled="disabled" @click="unlock(row)">修改选择 / 按当前价重新预览</el-button>
      </template>
      <template v-else-if="row.automatic && !row.legacy">
        <el-form-item v-for="f in row.fields" :key="f.id" :label="f.fieldName">
          <el-select v-if="f.fieldType==='single'" v-model="row.attributes[f.id]" clearable :disabled="disabled" @change="invalidate(row)"><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select>
          <el-input v-else v-model="row.attributes[f.id]" placeholder="未知留空；填写实际值" :disabled="disabled" @input="invalidate(row)" />
        </el-form-item>
        <el-button :disabled="disabled||row.busy" @click="autoQuote(row)">重新取价</el-button>
      </template>
      <template v-else-if="row.config && !row.legacy">
        <el-form-item label="基础价 / 成套价" required><el-select v-model="row.choice.baseId" :disabled="disabled" @change="()=>baseChanged(row)"><el-option v-for="b in row.config.bases.filter(b=>b.productId===productId)" :key="b.id" :label="`${b.name} · ${b.price}元/${b.priceUnit}`" :value="b.id" /></el-select></el-form-item>
        <el-form-item v-for="g in row.config.groups" :key="g.id" :label="g.name" required>
          <el-select v-model="row.choice.values[g.id]" :disabled="disabled || !base(row)" @change="quote(row)"><el-option v-for="o in g.options.filter(o=>(base(row)?.allowed[g.id]||[]).includes(o.id))" :key="o.id" :label="`${o.name} · ${base(row)?.included.includes(o.id)?'已包含':('加'+o.extra+'元')}`" :value="o.id" /></el-select>
        </el-form-item>
      </template>
      <p v-if="row.busy">正在计算…</p><p v-if="row.error" role="alert" class="processing-error">{{ row.error }}</p>
      <div v-if="row.result">
        <p>{{ row.result.baseName }}：{{ row.result.basePrice }}元/{{ row.result.priceUnit }}</p>
        <p v-for="(line,i) in row.result.lines" :key="i">{{ line.group }}：{{ line.option }} · {{ line.included?'已包含，不另加价':('加'+line.extra+'元/'+row.result.priceUnit) }}</p>
        <strong>本单单价 {{ row.result.unitPrice }}元/{{ row.result.priceUnit }}</strong>
        <p>每{{ row.result.priceUnit }}对应{{ row.result.unitsPerPriceUnit }}个产品报工单位；不良扣价{{ row.result.deductPrice }}元/{{ row.result.priceUnit }}。</p>
      </div>
    </el-card>
  </section>
</template>
<script setup>
import { ref, watch } from 'vue'
import { getCustomFields } from '@/api/prod'
import { previewAttributePrice } from '@/api/operationPriceList'
import { getProductProcessing, previewProcessing } from '@/api/operationPricing'
const props=defineProps({productId:Number,tasks:{type:Array,default:()=>[]},copy:Boolean,disabled:Boolean})
let craftFields=[]
const rows=ref([]),loading=ref(false),error=ref('');let loadNumber=0
const base=r=>r.config?.bases.find(b=>b.id===r.choice.baseId && b.productId===props.productId)
watch(()=>props.productId,load,{immediate:true})
async function load(){const n=++loadNumber; rows.value=[];error.value='';if(!props.productId){loading.value=false;return}loading.value=true
  try{const [res,fieldsResult]=await Promise.all([getProductProcessing(props.productId),getCustomFields('work_order_task')]);craftFields=fieldsResult.data||[];if(n!==loadNumber)return
    rows.value=(res.data||[]).map(op=>{const task=props.tasks.find(t=>t.operationId===op.id);const old=task?.processingPrice;return {id:op.id,name:op.name,automatic:!!op.automatic,fields:craftFields.filter(f=>f.operationId===op.id),attributes:JSON.parse(JSON.stringify(task?.operationAttributes||{})),config:op.config,legacy:!!task?.hasReports&&!old&&!props.copy,choice:old?JSON.parse(JSON.stringify(old.choice)):{baseId:'',values:{}},keep:!!old&&!props.copy,result:old&&!props.copy?old:null,error:'',busy:false,revision:0}})
    for(const t of props.tasks.filter(t=>t.processingPrice && !rows.value.some(r=>r.id===t.operationId)))rows.value.push({id:t.operationId,name:t.operationName,keep:!props.copy,config:null,choice:JSON.parse(JSON.stringify(t.processingPrice.choice)),result:props.copy?null:t.processingPrice,error:props.copy?'原计价配置不在当前工艺中，请核查':''})
    await Promise.all(rows.value.filter(r=>r.automatic&&!r.keep&&!r.legacy).map(autoQuote))
    if(props.copy) await Promise.all(rows.value.filter(r=>r.config&&r.choice.baseId).map(quote))
  }catch(e){if(n===loadNumber)error.value=e.message||'加工计价加载失败'}finally{if(n===loadNumber)loading.value=false}}
function invalidate(r){r.revision++;r.busy=false;r.choice.quoteToken=null;r.result=null;r.error='工艺已修改，请重新取价'}
async function autoQuote(r){const n=++r.revision;r.result=null;r.choice.quoteToken=null;r.busy=true;r.error='';try{const res=await previewAttributePrice({productId:props.productId,operationId:r.id,operationAttributes:r.attributes});if(n!==r.revision)return;r.result=res.data;r.choice={...res.data.choice,quoteToken:res.data.quoteToken}}catch(e){if(n===r.revision)r.error=e.message||'自动取价失败'}finally{if(n===r.revision)r.busy=false}}
function unlock(r){r.keep=false;r.result=null;r.choice.quoteToken=null;quote(r)}
function baseChanged(r){r.choice.values={};quote(r)}
async function quote(r){const n=++r.revision;r.busy=false;r.choice.quoteToken=null;r.result=null;r.error='';if(!base(r)){r.error='请选适用于当前产品的基础价';return}
  if(r.config.groups.some(g=>!r.choice.values[g.id])){r.error='请为每个加工组选一个值';return}r.busy=true
  try{const res=await previewProcessing(r.id,props.productId,r.choice);if(n!==r.revision)return;r.result=res.data;r.choice.quoteToken=res.data.quoteToken}
  catch(e){if(n===r.revision)r.error=e.message||'计价失败，请重新选择'}finally{if(n===r.revision)r.busy=false}}
function getChoices(){const active=rows.value.filter(r=>!r.legacy);if(loading.value||error.value||active.some(r=>r.busy||r.error||!r.result))throw new Error('请先补齐加工选择并完成计价预览');return Object.fromEntries(active.map(r=>[r.id,r.keep?{...r.result.choice,quoteToken:r.result.quoteToken}:r.choice]))}
function getOperationAttributes(){return Object.fromEntries(rows.value.filter(r=>r.automatic&&!r.keep&&!r.legacy).map(r=>[r.id,r.attributes]))}
defineExpose({getChoices,getOperationAttributes})
</script>
<style scoped>.processing-task{margin:12px 0}.order-processing p{font-size:13px;color:#606266;line-height:1.7}.order-processing .processing-error{color:#c45656}.order-processing .el-select{width:100%}</style>
