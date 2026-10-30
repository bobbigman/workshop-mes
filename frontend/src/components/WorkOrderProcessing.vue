<template>
  <section v-if="loading || error || rows.length" class="order-processing">
    <h3>本单工序工价</h3>
    <p>按产品规格与工序属性自动取价；提交或保存后单价定下来不可再改，暂存可不齐全；已有报工后不可更改。</p>
    <p v-if="loading">正在获取工序单价…</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-button v-if="error" @click="load">重试</el-button>
    <el-card v-for="row in rows" :key="row.id" shadow="never" class="processing-task">
      <h4>{{ row.name }}</h4>
      <template v-if="row.keep">
        <el-tag>本单单价已定</el-tag>
      </template>
      <template v-else>
        <el-form-item v-for="f in row.fields" :key="f.id" :label="f.fieldName">
          <el-select v-if="f.fieldType==='single'" v-model="row.attributes[f.id]" clearable :disabled="disabled" @change="invalidate(row)"><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select>
          <el-input v-else v-model="row.attributes[f.id]" placeholder="不确定就留空；确定的填实际数值。" :disabled="disabled" @input="invalidate(row)" />
        </el-form-item>
        <el-button :disabled="disabled||row.busy" @click="autoQuote(row)">重新取价</el-button>
      </template>
      <p v-if="row.busy">正在计算单价…</p><p v-if="row.error" role="alert" class="processing-error">{{ row.error }}</p>
      <div v-if="row.result">
        <p>{{ row.result.baseName }}：{{ row.result.basePrice }}元/{{ row.result.priceUnit }}</p>
        <p v-for="(line,i) in row.result.lines" :key="i">{{ line.group }}：{{ line.option }} · {{ line.included?'含在内，不加钱。':('加'+line.extra+'元/'+row.result.priceUnit) }}</p>
        <strong>本单单价 {{ row.result.unitPrice }}元/{{ row.result.priceUnit }}</strong>
        <p>每{{ row.result.priceUnit }}对应{{ row.result.unitsPerPriceUnit }}个产品报工单位；不良扣价{{ row.result.deductPrice }}元/{{ row.result.priceUnit }}。</p>
      </div>
    </el-card>
  </section>
</template>
<script setup>
import { ref, watch } from 'vue'
import { getCustomFields } from '@/api/prod'
import { getProductOperations, previewAttributePrice } from '@/api/operationPriceList'
const props=defineProps({productId:Number,tasks:{type:Array,default:()=>[]},copy:Boolean,disabled:Boolean})
const rows=ref([]),loading=ref(false),error=ref('');let loadNumber=0
watch(()=>props.productId,load,{immediate:true})
async function load(){const n=++loadNumber;rows.value=[];error.value='';if(!props.productId){loading.value=false;return}loading.value=true
  try{const [opsRes,fieldsResult]=await Promise.all([getProductOperations(props.productId),getCustomFields('work_order_task')]);const craftFields=fieldsResult.data||[];if(n!==loadNumber)return
    rows.value=(opsRes.data||[]).map(op=>{const task=props.tasks.find(t=>t.operationId===op.id);const old=task?.processingPrice;return {id:op.id,name:op.name,fields:craftFields.filter(f=>f.operationId===op.id),attributes:JSON.parse(JSON.stringify(task?.operationAttributes||{})),keep:!!old&&!props.copy,result:old&&!props.copy?old:null,quoteToken:null,error:'',busy:false,revision:0}})
    await Promise.all(rows.value.filter(r=>!r.keep).map(autoQuote))
  }catch(e){if(n===loadNumber)error.value=e.message||'单价数据获取失败，请检查工序价目表是否已设置且在有效期内，或检查网络连接。'}finally{if(n===loadNumber)loading.value=false}}
function invalidate(r){r.revision++;r.busy=false;r.quoteToken=null;r.result=null;r.error='工艺路线已变更导致原报价失效，请点击「重新取价」刷新单价。'}
async function autoQuote(r){const n=++r.revision;r.result=null;r.quoteToken=null;r.busy=true;r.error='';try{const res=await previewAttributePrice({productId:props.productId,operationId:r.id,operationAttributes:r.attributes});if(n!==r.revision)return;r.result=res.data;r.quoteToken=res.data.quoteToken}catch(e){if(n===r.revision)r.error=e.message||'自动获取单价失败，请检查该工序的属性是否完整、工序价目表是否已启用且在有效期内。'}finally{if(n===r.revision)r.busy=false}}
/** strict=true：提交/正式保存须全部预览成功；false：暂存允许缺价 */
function getQuoteTokens(strict=true){
  const active=rows.value
  if(strict){
    if(loading.value||error.value||active.some(r=>r.busy||r.error||!r.result))throw new Error('提交前需确保所有工序的单价预览已完成，请补齐缺失的属性值后重新取价。')
    return Object.fromEntries(active.map(r=>[r.id,r.keep?r.result.quoteToken:r.quoteToken]))
  }
  return Object.fromEntries(active.filter(r=>r.result&&(r.keep||r.quoteToken)).map(r=>[r.id,r.keep?r.result.quoteToken:r.quoteToken]))
}
function getOperationAttributes(){return Object.fromEntries(rows.value.filter(r=>!r.keep).map(r=>[r.id,r.attributes]))}
defineExpose({getQuoteTokens,getOperationAttributes})
</script>
<style scoped>.processing-task{margin:12px 0}.order-processing p{font-size:13px;color:#606266;line-height:1.7}.order-processing .processing-error{color:#c45656}.order-processing .el-select{width:100%}</style>
