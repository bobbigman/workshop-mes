<template>
  <el-dialog :model-value="modelValue" title="调整工价" width="920px" :close-on-click-modal="false" :before-close="close" @update:model-value="emit('update:modelValue',$event)">
    <div v-loading="busy">
      <el-alert title="这里只能调整工价金额和不良扣价。报工按业务日期取对应工价；若勾选本月追溯，会一并重算受影响的报工和工资明细，已确认的工资单会退回草稿，需要重新确认。" type="info" :closable="false" show-icon />
      <p>{{ table?.code }} · {{ table?.name }}</p>
      <el-form label-width="110px" :disabled="busy || retryPending">
        <el-form-item label="生效日期" required><el-date-picker v-model="form.effectiveFrom" value-format="YYYY-MM-DD" /><el-button link @click="form.effectiveFrom=today">今天</el-button><el-button link @click="form.effectiveFrom=today.slice(0,8)+'01';form.repriceCurrentMonth=true">本月1日</el-button></el-form-item>
        <el-form-item label="追溯范围"><el-checkbox v-model="form.repriceCurrentMonth">重算生效日到本月底之间已有的报工与工资；勾选后，已确认的工资单会退回草稿。</el-checkbox></el-form-item>
        <el-form-item label="调整原因" :required="form.repriceCurrentMonth"><el-input v-model="form.reason" maxlength="500" show-word-limit /></el-form-item>
        <mes-table table-key="views/operation-price-list/PriceChangeDialog:1" :data="form.rows" border max-height="260">
          <el-table-column prop="label" label="价格行" min-width="150" />
          <el-table-column prop="oldAmount" label="原金额" width="100" />
          <el-table-column label="新金额" width="195"><template #default="{row}"><el-input-number v-model="row.amount" :precision="4" :min="0" :max="1000000" /></template></el-table-column>
          <el-table-column prop="oldDeductPrice" label="原扣价" width="100" />
          <el-table-column label="新扣价" width="195"><template #default="{row}"><el-input-number v-model="row.deductPrice" :disabled="row.kind==='extra'" :precision="4" :min="0" :max="1000000" /></template></el-table-column>
        </mes-table>
      </el-form>
      <template v-if="preview">
        <p>影响 {{ preview.reportCount }} 条报工、{{ preview.employeeCount }} 人、{{ preview.statementCount }} 张工资单。</p>
        <p>报工金额：{{ money(preview.reportOldAmount) }} → {{ money(preview.reportNewAmount) }}；工资明细净额：{{ money(preview.oldAmount) }} → {{ money(preview.newAmount) }}（差额 {{ money(preview.newAmount-preview.oldAmount) }}）</p>
        <el-alert v-if="preview.revokeStatementIds?.length" :title="`确认调整后，确认调整后，以下已确认工资单将退回草稿：${preview.revokeStatementIds.join('、')}`" type="warning" :closable="false" />
        <el-alert v-for="(message,i) in preview.blockers" :key="i" :title="message" type="error" :closable="false" />
        <mes-table table-key="views/operation-price-list/PriceChangeDialog:2" :data="preview.rows" border max-height="220">
          <el-table-column prop="reportId" label="报工编号" /><el-table-column prop="userId" label="员工编号" />
          <el-table-column label="业务日期"><template #default="{row}">{{ row.reportTime?.slice(0,10) }}</template></el-table-column>
          <el-table-column prop="oldReportAmount" label="原报工金额" /><el-table-column prop="newReportAmount" label="新报工金额" />
          <el-table-column prop="statementId" label="工资单编号" />
        </mes-table>
        <p v-if="preview.previewToken">预览结果5分钟内有效；期间若价格、报工或工资有变动，请重新点「预览影响」查看最新结果。</p>
      </template>
      <el-alert v-if="retryPending" title="上次提交的结果还没确认。请点上方「确认上次提交」确认提交结果，避免重复调价。" type="warning" :closable="false" />
    </div>
    <template #footer>
      <el-button :disabled="busy||retryPending" @click="close">取消</el-button>
      <el-button :disabled="busy||retryPending" @click="prepare">预览影响</el-button>
      <el-button type="primary" :loading="busy" :disabled="!preview?.previewToken||!!preview?.blockers?.length" @click="commit">{{retryPending?'确认上次提交':'确认调整'}}</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getPriceList, previewPriceChange, commitPriceChange } from '@/api/operationPriceList'
const props=defineProps({modelValue:Boolean,tableId:[Number,String]})
const emit=defineEmits(['update:modelValue','done'])
const busy=ref(false),table=ref(null),preview=ref(null),retryPending=ref(false)
const today=new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Shanghai'}).format(new Date())
const form=ref({effectiveFrom:today,repriceCurrentMonth:false,reason:'',rows:[]})
let requestId=''
const money=v=>Number(v??0).toFixed(2)
watch(form,()=>{preview.value=null;requestId='10000000-1000-4000-8000-100000000000'.replace(/[018]/g,c=>(Number(c)^crypto.getRandomValues(new Uint8Array(1))[0]&15>>Number(c)/4).toString(16))},{deep:true,flush:'sync'})
watch(()=>props.modelValue,async visible=>{
  if(!visible)return
  busy.value=true;preview.value=null;retryPending.value=false;table.value=null
  try{
    const {data}=await getPriceList(props.tableId);table.value=data
    form.value={effectiveFrom:today,repriceCurrentMonth:false,reason:'',rows:data.details.rows.map(r=>({...r,label:r.label||r.rowId,oldAmount:r.amount,oldDeductPrice:r.deductPrice}))}
  }finally{busy.value=false}
})
async function prepare(){
  if(!table.value)return
  busy.value=true;preview.value=null
  try{
    const {data}=await previewPriceChange(props.tableId,{...form.value,requestId,expectedVersion:table.value.version,rows:form.value.rows.map(r=>({rowId:r.rowId,amount:r.amount,deductPrice:r.deductPrice}))})
    preview.value=data
  }finally{busy.value=false}
}
async function commit(){
  busy.value=true
  try{
    const {data}=await commitPriceChange(props.tableId,{requestId,previewToken:preview.value.previewToken,planHash:preview.value.planHash})
    retryPending.value=false
    ElMessage.success(`调整完成：共更新报工${data.result.reportCount}条、工资单${data.result.statementCount}张（批次${data.batchId}）`)
    emit('update:modelValue',false);emit('done',data.batchId)
  }catch(error){
    // 网络超时不能假定未提交；重试保持同一幂等请求。
    retryPending.value=!error.response || error.response.status>=500
    throw error
  }finally{busy.value=false}
}
function close(){if(!busy.value&&!retryPending.value)emit('update:modelValue',false)}
</script>
