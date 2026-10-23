<template>
  <el-dialog :model-value="modelValue" :title="`${operationName} · 加工计价`" width="min(1100px, 96vw)" :close-on-click-modal="false" :show-close="!saving" @update:model-value="$emit('update:modelValue', $event)">
    <el-alert title="启用后，该工序的新工单按此计件价锁价，替代旧工价表的部门/人员价格；人员工资系数继续适用。已保存工单不随调价改变。" type="warning" :closable="false" show-icon />
    <p v-if="error" role="alert">{{ error }} <el-button @click="load">重新加载</el-button></p>
    <el-form v-else v-loading="loading" :disabled="saving || loading" label-position="top">
      <h3>加工单选组 <el-button @click="addGroup">＋新增组</el-button></h3>
      <p>每组必选一个；多个组的加价相加。“无此加工”请明确设为0元。未确认的加工不要录入正式计价资料。</p>
      <el-card v-for="(group, gi) in config.groups" :key="group.id" shadow="never" class="pricing-group">
        <div class="pricing-row"><el-input v-model="group.name" placeholder="组名，例如：丝印" maxlength="128" /><el-button type="danger" plain @click="removeGroup(gi)">删除组</el-button></div>
        <div v-for="(option, oi) in group.options" :key="option.id" class="pricing-row">
          <el-input v-model="option.name" placeholder="选项名，例如：1片丝印" maxlength="128" />
          <el-input-number v-model="option.extra" :min="0" :max="1000000" :precision="4" aria-label="选项加价" /><span>元/计价单位</span>
          <el-button link type="danger" @click="removeOption(group, oi)">删除</el-button>
        </div>
        <el-button @click="group.options.push({id:uid(), name:'', extra:null})">＋新增选项</el-button>
      </el-card>
      <h3>基础价 / 成套价 <el-button @click="addBase">＋新增基础价</el-button></h3>
      <el-card v-for="(base, bi) in config.bases" :key="base.id" shadow="never" class="pricing-group">
        <div class="pricing-row"><el-input v-model="base.name" placeholder="价格名称，例如：25寸8K / 方形成套价" maxlength="128" /><el-button type="danger" plain @click="config.bases.splice(bi,1)">删除基础价</el-button></div>
        <div class="pricing-row">
          <el-form-item label="产品" required><el-select v-model="base.productId" filterable @change="setUnit(base)"><el-option v-for="p in products" :key="p.id" :label="`${p.code} ${p.name}`" :value="p.id" /></el-select></el-form-item>
          <el-form-item label="基础单价" required><el-input-number v-model="base.price" :min="0" :max="1000000" :precision="4" /></el-form-item>
          <el-form-item label="不良扣价（同计价单位）"><el-input-number v-model="base.deductPrice" :min="0" :max="1000000" :precision="4" /></el-form-item>
        </div>
        <div class="pricing-row">
          <el-form-item label="计价单位" required><el-input v-model="base.priceUnit" placeholder="例如：打" maxlength="128" /></el-form-item>
          <el-form-item label="每1计价单位对应的报工数量" required><el-input-number v-model="base.unitsPerPriceUnit" :min="0.0001" :max="1000000" :precision="4" /></el-form-item>
          <span>报工单位：{{ units.find(u=>u.id===base.reportUnitId)?.name || '请先给产品设置单位' }}</span>
        </div>
        <p>例如：产品按“把”报工，每打12把，填12；产品按“打”报工，填1。附加价和扣价均使用此计价单位。</p>
        <div v-for="group in config.groups" :key="group.id" class="pricing-row">
          <strong>{{ group.name || '未命名组' }}</strong>
          <el-form-item label="允许选择（必填）"><el-select v-model="base.allowed[group.id]" multiple @change="cleanIncluded(base)"><el-option v-for="o in group.options" :key="o.id" :label="o.name || '未命名选项'" :value="o.id" /></el-select></el-form-item>
          <el-form-item label="基础价已含这些值（不再加价）"><el-select :model-value="base.included.filter(id=>group.options.some(o=>o.id===id))" multiple @update:model-value="v=>setIncluded(base,group,v)"><el-option v-for="o in group.options.filter(o=>(base.allowed[group.id]||[]).includes(o.id))" :key="o.id" :label="o.name || '未命名选项'" :value="o.id" /></el-select></el-form-item>
        </div>
      </el-card>
    </el-form>
    <template #footer><el-button :disabled="saving" @click="$emit('update:modelValue',false)">取消</el-button><el-button type="primary" :disabled="loading || !!error" :loading="saving" @click="save">保存计价资料</el-button></template>
  </el-dialog>
</template>
<script setup>
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getProcessingConfig, saveProcessingConfig } from '@/api/operationPricing'
import { getProductList, getProduct, getUnitList } from '@/api/base'
const props = defineProps({modelValue:Boolean, operationId:Number, operationName:String})
const emit = defineEmits(['update:modelValue'])
const config=ref({version:'',groups:[],bases:[]}), products=ref([]), units=ref([]), loading=ref(false), saving=ref(false), error=ref('')
const uid=()=>crypto.randomUUID()
watch(()=>[props.modelValue,props.operationId],()=>{if(props.modelValue) load()})
async function load(){ loading.value=true; error.value=''; try {
  const [c,p,u]=await Promise.all([getProcessingConfig(props.operationId),getProductList({page:1,pageSize:500}),getUnitList()])
  config.value=c.data || {version:'',groups:[],bases:[]}; products.value=p.data?.list || []; units.value=u.data?.list || u.data || []
  const missing=[...new Set(config.value.bases.map(b=>b.productId))].filter(id=>!products.value.some(p=>p.id===id))
  for(const id of missing){const res=await getProduct(id);products.value.push(res.data)}
} catch(e){error.value=e.message || '加载加工计价失败'} finally {loading.value=false} }
function addGroup(){const id=uid(); config.value.groups.push({id,name:'',options:[]}); for(const b of config.value.bases)b.allowed[id]=[]}
function removeGroup(i){const g=config.value.groups[i]; for(const b of config.value.bases){delete b.allowed[g.id]; b.included=b.included.filter(id=>!g.options.some(o=>o.id===id))} config.value.groups.splice(i,1)}
function removeOption(g,i){const id=g.options[i].id;g.options.splice(i,1);for(const b of config.value.bases){b.allowed[g.id]=(b.allowed[g.id]||[]).filter(v=>v!==id);b.included=b.included.filter(v=>v!==id)}}
function addBase(){config.value.bases.push({id:uid(),name:'',productId:null,price:null,deductPrice:0,priceUnit:'',reportUnitId:null,unitsPerPriceUnit:1,allowed:Object.fromEntries(config.value.groups.map(g=>[g.id,[]])),included:[]})}
function setUnit(b){b.reportUnitId=products.value.find(p=>p.id===b.productId)?.unitId || null}
function cleanIncluded(b){const allowed=Object.values(b.allowed).flat();b.included=b.included.filter(id=>allowed.includes(id))}
function setIncluded(b,g,v){b.included=[...b.included.filter(id=>!g.options.some(o=>o.id===id)),...v]}
async function save(){if(saving.value)return;saving.value=true;try{await saveProcessingConfig(props.operationId,config.value);ElMessage.success('计价资料已保存，仅新定价使用');emit('update:modelValue',false)}finally{saving.value=false}}
</script>
<style scoped>.pricing-group{margin:12px 0}.pricing-row{display:flex;flex-wrap:wrap;align-items:center;gap:12px;margin:12px 0}.pricing-row>.el-input{max-width:420px}.pricing-row .el-select{min-width:230px}.pricing-row .el-form-item{margin-bottom:0}p{font-size:13px;color:#606266;line-height:1.7}</style>
