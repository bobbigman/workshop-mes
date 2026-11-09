<template>
  <section v-if="loading || error || rows.length" class="order-processing">
    <h3>本单工序工价</h3>
    <p v-if="approveMode">选产品后补全带 ★ 的工艺属性，再点底栏「重新取价」。要锁价：先「保存」，再在本单或列表点「审核」；暂存/保存可不取齐价。</p>
    <p v-else>本单已审核锁价，工艺属性不可改。改数量、交期或计划后点「保存」即可。</p>
    <p v-if="loading">正在加载工序…</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-button v-if="error" size="small" @click="load">重试</el-button>
    <el-table v-if="rows.length" :data="rows" border size="small" class="processing-table">
      <el-table-column prop="name" label="工序" width="88" />
      <el-table-column label="工艺属性" min-width="420">
        <template #default="{ row }">
          <template v-if="row.keep"><span class="muted">已锁价，不可改</span></template>
          <div v-else-if="row.fields.length" class="craft-cell">
            <div v-for="f in row.fields" :key="f.id" class="craft-row" :class="{ 'is-error': !!row.fieldErrors[f.id] }">
              <span class="field-label">
                <span v-if="isRequired(row, f.id)" class="req-star" title="取价必填" aria-label="取价必填">★</span>
                <span>{{ f.fieldName }}</span>
              </span>
              <div class="field-control">
                <div class="field-input-row">
                  <el-select
                    v-if="f.fieldType==='single'"
                    v-model="row.attributes[f.id]"
                    clearable
                    size="small"
                    :disabled="disabled"
                    placeholder="选择"
                    @change="onFieldChange(row, f.id)"
                  >
                    <el-option v-for="v in f.options" :key="v" :value="v" :label="v" />
                  </el-select>
                  <el-input
                    v-else
                    v-model="row.attributes[f.id]"
                    size="small"
                    :disabled="disabled"
                    placeholder="可留空"
                    @input="onFieldChange(row, f.id)"
                  />
                  <el-tooltip
                    v-if="row.fieldErrors[f.id]"
                    :content="row.fieldErrorTips[f.id] || row.fieldErrors[f.id]"
                    placement="top"
                    effect="dark"
                  >
                    <el-icon class="field-warn" aria-label="取价校验失败"><WarningFilled /></el-icon>
                  </el-tooltip>
                </div>
              </div>
            </div>
          </div>
          <span v-else class="muted">无</span>
        </template>
      </el-table-column>
      <el-table-column label="本单单价" min-width="160">
        <template #default="{ row }">
          <template v-if="row.keep">
            <el-tag size="small" type="success">本单单价已定</el-tag>
            <div v-if="row.result" class="price-block">
              <strong>{{ row.result.unitPrice }}元/{{ row.result.priceUnit }}</strong>
            </div>
          </template>
          <template v-else>
            <p v-if="row.busy" class="processing-hint">正在计算…</p>
            <p v-else-if="hasFieldErrors(row)" role="alert" class="processing-error">有属性未补全，见左侧感叹号</p>
            <p v-else-if="row.error" role="alert" class="processing-error">{{ row.error }}</p>
            <p v-else-if="row.hint" class="processing-hint">{{ row.hint }}</p>
            <div v-if="row.result" class="price-block">
              <div class="muted">{{ row.result.baseName }}</div>
              <strong>{{ row.result.unitPrice }}元/{{ row.result.priceUnit }}</strong>
              <div v-for="(line,i) in row.result.lines" :key="i" class="muted">
                {{ line.group }}：{{ line.option }}{{ line.included ? '（已含）' : ` +${line.extra}` }}
              </div>
            </div>
          </template>
        </template>
      </el-table-column>
    </el-table>
    <div v-if="rows.length" class="price-summary">
      <span class="price-summary-status">已取价 {{ priceSummary.quoted }} / {{ priceSummary.total }}</span>
      <span v-if="!priceSummary.quoted" class="muted">工序单价合计 —</span>
      <span v-else-if="priceSummary.sameUnit" class="price-summary-total">
        工序单价合计
        <strong>{{ money(priceSummary.primarySum) }}</strong>
        元/{{ priceSummary.primaryUnit }}
      </span>
      <span v-else class="price-summary-total">
        工序单价合计
        <template v-for="(u, i) in priceSummary.units" :key="u">
          <strong>{{ money(priceSummary.byUnit[u]) }}</strong> 元/{{ u }}<template v-if="i < priceSummary.units.length - 1"> + </template>
        </template>
        <span class="muted">（计价单位不同，分项显示）</span>
      </span>
    </div>
  </section>
</template>
<script setup>
import { ref, watch, computed, nextTick } from 'vue'
import { WarningFilled } from '@element-plus/icons-vue'
import { getCustomFields } from '@/api/prod'
import { getProductOperations, previewAttributePrice } from '@/api/operationPriceList'
const props=defineProps({
  productId: Number,
  tasks: { type: Array, default: () => [] },
  copy: Boolean,
  disabled: Boolean,
  /** 新建/草稿/待审：可改工艺取价；已生效单为 false */
  approveMode: { type: Boolean, default: true }
})
const rows=ref([]),loading=ref(false),error=ref('');let loadNumber=0
const QUOTE_HINT='请点底栏「重新取价」获取单价。'
const PENDING_CRAFT_HINT='补全带 ★ 的工艺属性后，再点底栏「重新取价」。'
const CHANGED_HINT='属性已改，请点底栏「重新取价」。'
watch(()=>props.productId,load,{immediate:true})
function money(n){const x=Number(n);return Number.isFinite(x)?(Math.round(x*100)/100).toFixed(2):'—'}
const priceSummary=computed(()=>{
  const list=rows.value||[]
  const quoted=list.filter(r=>r.result!=null&&r.result.unitPrice!=null)
  const byUnit={}
  for(const r of quoted){
    const u=r.result.priceUnit||'单位'
    byUnit[u]=(byUnit[u]||0)+Number(r.result.unitPrice)
  }
  const units=Object.keys(byUnit)
  return {
    quoted:quoted.length,
    total:list.length,
    byUnit,
    units,
    sameUnit:units.length===1,
    primaryUnit:units[0]||'',
    primarySum:units.length===1?byUnit[units[0]]:null
  }
})
const hasQuoteable=computed(()=>(rows.value||[]).some(r=>!r.keep))
const quoting=computed(()=>(rows.value||[]).some(r=>r.busy))
function hasFilledAttr(r){return Object.values(r.attributes||{}).some(v=>v!=null&&String(v).trim()!=='')}
function pendingHint(r){return r.fields.length&&!hasFilledAttr(r)?PENDING_CRAFT_HINT:QUOTE_HINT}
function isRequired(row, fieldId){return (row.requiredFieldIds||[]).map(Number).includes(Number(fieldId))}
function hasFieldErrors(row){return Object.keys(row.fieldErrors||{}).length>0}
function onFieldChange(row, fieldId){
  if(row.fieldErrors?.[fieldId]){
    const next={...row.fieldErrors};delete next[fieldId];row.fieldErrors=next
    const tips={...row.fieldErrorTips};delete tips[fieldId];row.fieldErrorTips=tips
  }
  invalidate(row)
}
async function load(){const n=++loadNumber;rows.value=[];error.value='';if(!props.productId){loading.value=false;return}loading.value=true
  try{const [opsRes,fieldsResult]=await Promise.all([getProductOperations(props.productId),getCustomFields('work_order_task')]);const craftFields=fieldsResult.data||[];if(n!==loadNumber)return
    rows.value=(opsRes.data||[]).map(op=>{const task=props.tasks.find(t=>t.operationId===op.id);const old=task?.processingPrice;return {id:op.id,name:op.name,requiredFieldIds:op.requiredFieldIds||[],fields:craftFields.filter(f=>f.operationId===op.id),attributes:JSON.parse(JSON.stringify(task?.operationAttributes||{})),keep:!!old&&!props.copy,result:old&&!props.copy?old:null,quoteToken:null,error:'',hint:'',fieldErrors:{},fieldErrorTips:{},busy:false,revision:0}})
    for(const r of rows.value){if(!r.keep)r.hint=pendingHint(r)}
  }catch(e){if(n===loadNumber)error.value=e.message||'工序数据获取失败，请检查网络或工艺路线是否已配置。'}finally{if(n===loadNumber)loading.value=false}}
function invalidate(r){r.revision++;r.busy=false;r.quoteToken=null;r.result=null;r.error='';r.hint=CHANGED_HINT}
async function autoQuote(r){const n=++r.revision;r.result=null;r.quoteToken=null;r.busy=true;r.error='';r.hint='';r.fieldErrors={};r.fieldErrorTips={}
  for(const id of r.requiredFieldIds||[]){
    const v=r.attributes?.[id]
    if(v==null||String(v).trim()===''){
      r.fieldErrors[id]='请补全后再取价'
      r.fieldErrorTips[id]='请补全后再取价'
    }
  }
  try{
    const res=await previewAttributePrice({productId:props.productId,operationId:r.id,operationAttributes:r.attributes})
    if(n!==r.revision)return
    r.result=res.data;r.quoteToken=res.data.quoteToken;r.fieldErrors={};r.fieldErrorTips={}
  }catch(e){
    if(n!==r.revision)return
    const msg=e.message||'自动获取单价失败，请检查该工序的属性是否完整、工序价目表是否已启用且在有效期内。'
    if(e.fieldId!=null){
      r.fieldErrors={...r.fieldErrors,[e.fieldId]:'请补全或改正后再取价'}
      r.fieldErrorTips={...r.fieldErrorTips,[e.fieldId]:msg}
      r.error=''
    }else{
      r.error=msg
    }
  }finally{if(n===r.revision)r.busy=false}}
async function ensureQuotes(){
  const pending=rows.value.filter(r=>!r.keep&&!r.result)
  if(!pending.length)return
  await Promise.all(pending.map(autoQuote))
}
/** 底栏「重新取价」：对本单所有未锁价工序重新取价 */
async function requoteAll(){
  const targets=rows.value.filter(r=>!r.keep)
  if(!targets.length)return
  await Promise.all(targets.map(autoQuote))
  await scrollFirstError()
}
async function scrollFirstError(){
  await nextTick()
  document.querySelector('.order-processing .craft-row.is-error')?.scrollIntoView({ behavior:'smooth', block:'center' })
}
function getQuoteTokens(strict=true){
  const active=rows.value
  if(strict){
    if(loading.value)throw new Error('工序还在加载，请稍后再提交。')
    if(error.value)throw new Error(error.value)
    if(active.some(r=>r.busy))throw new Error('还有工序正在取价，请稍后再提交。')
    const bad=active.filter(r=>r.error||hasFieldErrors(r)||!r.result)
    if(bad.length){
      scrollFirstError()
      const parts=bad.map(r=>{
        if(hasFieldErrors(r))return `「${r.name}」：有属性未补全`
        if(r.error)return `「${r.name}」：${r.error}`
        return `「${r.name}」尚未取价成功，请补全带 ★ 的属性后点底栏「重新取价」`
      })
      throw new Error('提交或保存前须各工序取价成功：\n'+parts.join('\n'))
    }
    return Object.fromEntries(active.map(r=>[r.id,r.keep?r.result.quoteToken:r.quoteToken]))
  }
  return Object.fromEntries(active.filter(r=>r.result&&(r.keep||r.quoteToken)).map(r=>[r.id,r.keep?r.result.quoteToken:r.quoteToken]))
}
function getOperationAttributes(){return Object.fromEntries(rows.value.filter(r=>!r.keep).map(r=>[r.id,r.attributes]))}
defineExpose({getQuoteTokens,getOperationAttributes,ensureQuotes,requoteAll,hasQuoteable,quoting})
</script>
<style scoped>
.order-processing p{font-size:13px;color:#606266;line-height:1.6;margin:0 0 8px}
.order-processing h3{margin:0 0 6px;font-size:15px}
.processing-table{width:100%;margin-top:4px}
.processing-table :deep(.el-table__cell){vertical-align:top}
.craft-cell{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 10px;align-items:start}
.craft-row{display:grid;grid-template-columns:minmax(72px,40%) minmax(0,1fr);gap:4px 6px;align-items:start}
.field-label{display:inline-flex;align-items:flex-start;justify-content:flex-end;gap:3px;font-size:12px;color:#606266;line-height:24px;text-align:right;padding-top:1px}
.field-control{min-width:0}
.field-input-row{display:flex;align-items:center;gap:4px;min-width:0}
.field-input-row .el-select,.field-input-row .el-input{flex:1;min-width:0}
.field-warn{flex:none;color:var(--el-color-danger);font-size:16px;cursor:help}
.req-star{color:var(--el-color-danger);font-size:14px;font-weight:700;line-height:24px}
.processing-error{color:#c45656;font-size:12px;line-height:1.35;margin:2px 0 0;white-space:pre-wrap}
.processing-hint,.muted{color:#909399;font-size:12px;line-height:1.4;margin:0}
.price-block{margin-top:4px;font-size:12px;line-height:1.45}
.price-block strong{font-size:13px}
.price-summary{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:8px 16px;margin-top:10px;padding:10px 12px;background:#f7f9fc;border-radius:6px;font-size:13px;color:#606266}
.price-summary-status{color:#909399}
.price-summary-total strong{font-size:16px;font-weight:700;color:#303133;margin:0 2px}
.craft-row.is-error :deep(.el-input__wrapper),.craft-row.is-error :deep(.el-select__wrapper){box-shadow:0 0 0 1px var(--el-color-danger) inset}
@media (min-width:1000px){.craft-cell{grid-template-columns:repeat(3,minmax(0,1fr))}}
@media (max-width:720px){.craft-cell{grid-template-columns:1fr}}
</style>
