<template>
  <el-dialog :model-value="modelValue" title="调价记录" width="920px" @update:model-value="emit('update:modelValue',$event)">
    <div v-loading="busy">
      <el-table :data="rows" border>
        <el-table-column prop="id" label="批次" width="85" /><el-table-column prop="changedBy" label="操作者编号" width="110" />
        <el-table-column label="生效日" width="110"><template #default="{row}">{{row.effectiveFrom.slice(0,10)}}</template></el-table-column>
        <el-table-column prop="reason" label="原因" />
        <el-table-column label="本月追溯" width="90"><template #default="{row}">{{row.repriceCurrentMonth?'是':'否'}}</template></el-table-column>
        <el-table-column width="90"><template #default="{row}"><el-button link type="primary" @click="detail(row.id)">查看依据</el-button></template></el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" :total="total" :page-size="20" layout="prev,pager,next,total" @current-change="load" />
      <template v-if="selected">
        <p>批次 {{selected.id}} · {{selected.createdAt?.replace('T',' ')}} · {{selected.reason}}</p>
        <p>报工 {{after.reportCount}} 条，工资单 {{after.statementCount}} 张；工资明细净额 {{beforeAmount}} → {{afterAmount}}</p>
        <p v-if="after.revokeStatementIds?.length">撤回确认的工资单：{{after.revokeStatementIds.join('、')}}</p>
        <el-table :data="reportRows" border max-height="260">
          <el-table-column prop="id" label="报工编号" /><el-table-column prop="userId" label="员工编号" />
          <el-table-column prop="oldAmount" label="原报工金额" /><el-table-column prop="wageAmount" label="新报工金额" />
          <el-table-column prop="goodQty" label="良品数" /><el-table-column prop="durationMinutes" label="分钟" />
        </el-table>
        <el-collapse><el-collapse-item title="完整原始依据（价格、报工、工资与系数）"><pre class="audit-json">{{JSON.stringify(before,null,2)}}</pre></el-collapse-item><el-collapse-item title="完整调整结果"><pre class="audit-json">{{JSON.stringify(after,null,2)}}</pre></el-collapse-item></el-collapse>
      </template>
    </div>
  </el-dialog>
</template>
<script setup>
import {ref,computed,watch} from 'vue'
import {listPriceChanges,getPriceChange} from '@/api/operationPriceList'
const props=defineProps({modelValue:Boolean,tableId:[Number,String]})
const emit=defineEmits(['update:modelValue'])
const rows=ref([]),page=ref(1),total=ref(0),busy=ref(false),selected=ref(null),before=ref({}),after=ref({})
const reportRows=computed(()=>(after.value.reports||[]).map(r=>({...r,oldAmount:before.value.reports?.find(x=>x.id===r.id)?.wageAmount})))
const beforeAmount=computed(()=>Number(after.value.oldAmount??0).toFixed(2))
const afterAmount=computed(()=>Number(after.value.newAmount??0).toFixed(2))
watch(()=>props.modelValue,visible=>{if(visible){page.value=1;selected.value=null;load()}})
async function load(){busy.value=true;try{const {data}=await listPriceChanges(props.tableId,page.value);rows.value=data.list;total.value=data.total}finally{busy.value=false}}
async function detail(id){busy.value=true;try{const {data}=await getPriceChange(id);selected.value=data;before.value=JSON.parse(data.beforeJson);after.value=JSON.parse(data.afterJson)}finally{busy.value=false}}
</script>
<style scoped>.audit-json{max-height:320px;overflow:auto;white-space:pre-wrap;word-break:break-all;font-size:12px}</style>
