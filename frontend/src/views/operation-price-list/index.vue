<template>
  <div v-loading="loading">
    <template v-if="!editing">
      <h2>工序价目表</h2>
      <div class="toolbar">
        <el-input v-model="filters.keyword" clearable placeholder="编号 / 名称" style="width:190px" @keyup.enter="search" />
        <el-select v-model="filters.operationId" clearable placeholder="全部工序" style="width:150px"><el-option v-for="o in operations" :key="o.id" :label="o.name" :value="o.id" /></el-select>
        <el-select v-model="filters.status" clearable placeholder="全部状态" style="width:125px"><el-option v-for="(v,k) in statuses" :key="k" :label="v" :value="Number(k)" /></el-select>
        <el-date-picker v-model="filters.date" value-format="YYYY-MM-DD" placeholder="有效日期" style="width:160px" />
        <el-button @click="search">查询</el-button><el-button @click="reset">重置</el-button><el-button @click="load">刷新</el-button>
        <el-button type="primary" @click="create">＋ 新增价目表</el-button>
      </div>
      <el-table :data="list" border stripe>
        <el-table-column label="单据编号" min-width="150"><template #default="{row}"><el-button link type="primary" @click="open(row.id,true)">{{row.code}}</el-button></template></el-table-column>
        <el-table-column prop="name" label="名称" min-width="150" />
        <el-table-column label="价目表类型 / 工序" min-width="150"><template #default="{row}">{{opName(row.operationId)}}</template></el-table-column>
        <el-table-column label="计价方式" width="95"><template #default="{row}">{{pricingTypes[row.pricingType]}}</template></el-table-column>
        <el-table-column label="产品范围" min-width="180"><template #default="{row}">{{productName(row.productId)}}</template></el-table-column>
        <el-table-column label="明细行数" width="95"><template #default="{row}">{{row.details.rows.length}}</template></el-table-column>
        <el-table-column label="状态" width="95"><template #default="{row}"><el-tag :type="row.status===1?'success':row.status===2?'info':'warning'">{{statuses[row.status]}}</el-tag></template></el-table-column>
        <el-table-column label="生效 ~ 失效" min-width="205"><template #default="{row}">{{day(row.effectiveFrom)}} ~ {{row.effectiveTo?day(row.effectiveTo):'长期'}}</template></el-table-column>
        <el-table-column label="更新时间" min-width="160"><template #default="{row}">{{row.updatedAt?.replace('T',' ').slice(0,19)}}</template></el-table-column>
        <el-table-column label="操作" width="180" fixed="right"><template #default="{row}"><el-button link type="primary" @click="open(row.id,false)">编辑</el-button><el-button link @click="copy(row)">复制</el-button><el-button link type="danger" :disabled="row.status!==0||!!row.activatedAt" @click="removeTable(row)">删除</el-button></template></el-table-column>
      </el-table>
      <el-pagination v-model:current-page="filters.page" :page-size="20" :total="total" layout="prev,pager,next,total" @current-change="load" />
    </template>
    <template v-else>
      <div class="toolbar">
        <h2>工序价目表 <small>{{readonly?'浏览':id?'编辑':'新增'}}</small></h2>
        <el-button v-if="readonly" @click="readonly=false">编辑</el-button>
        <el-button v-if="!readonly" type="primary" :loading="saving" @click="save">保存</el-button>
        <el-button v-if="id&&form.status===1" :disabled="saving||dirty()" @click="setStatus(2)">停用</el-button>
        <el-button v-if="id&&form.status===2" :disabled="saving||dirty()" @click="setStatus(1)">启用</el-button>
        <el-button :disabled="saving" @click="back">返回列表</el-button>
      </div>
      <el-alert title="产品跟整张表走；无有效专属表才用通用表，专属表缺规格不会回通用表。" type="info" :closable="false" />
      <el-form :disabled="readonly||saving" label-width="110px" class="header-form">
        <el-form-item label="单据编号" required><el-input v-model="form.code" :disabled="!!id" maxlength="64" /></el-form-item>
        <el-form-item label="名称" required><el-input v-model="form.name" maxlength="128" /></el-form-item>
        <el-form-item label="价目表类型" required><el-select v-model="form.operationId" :disabled="!!form.activatedAt" filterable @change="operationChanged"><el-option v-for="o in operations" :key="o.id" :value="o.id" :label="o.name" /></el-select><span v-if="form.activatedAt" class="hint">首次启用后工序锁定</span></el-form-item>
        <el-form-item label="计价方式" required><el-select v-model="form.pricingType" :disabled="!!form.activatedAt" @change="pricingTypeChanged"><el-option v-for="(v,k) in pricingTypes" :key="Number(k)" :value="Number(k)" :label="v" /></el-select><span v-if="form.activatedAt" class="hint">首次启用后计价方式锁定</span></el-form-item>
        <el-form-item label="产品"><el-select v-model="form.productId" filterable clearable placeholder="通用表（不限产品）"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select></el-form-item>
        <el-form-item label="生效日期" required><el-date-picker v-model="form.effectiveFrom" value-format="YYYY-MM-DD" /></el-form-item>
        <el-form-item label="失效日期"><el-date-picker v-model="form.effectiveTo" value-format="YYYY-MM-DD" clearable /></el-form-item>
        <el-form-item label="计价单位" required><el-input v-model="form.priceUnit" placeholder="如：打" maxlength="32" /></el-form-item>
        <el-form-item label="报工单位" required><el-select v-model="form.reportUnitId"><el-option v-for="u in units" :key="u.id" :value="u.id" :label="u.name" /></el-select></el-form-item>
        <el-form-item label="换算数量" required><el-input-number v-model="form.unitsPerPriceUnit" :precision="4" :min="0.0001" :max="1000000" /><span class="hint">每计价单位对应报工数</span></el-form-item>
        <el-form-item label="状态"><el-tag>{{statuses[form.status]}}</el-tag></el-form-item>
        <el-form-item label="备注" class="wide"><el-input v-model="form.remark" maxlength="1000" placeholder="整表口径说明" /></el-form-item>
      </el-form>
      <el-form :disabled="readonly||saving">
        <el-collapse v-model="expanded"><el-collapse-item title="收费组（组内唯一，不同适用组按已确认规则累计）" name="groups">
          <el-button @click="addGroup">新增收费组</el-button>
          <el-table :data="form.details.groups" border>
            <el-table-column label="编码" width="150"><template #default="{row}"><el-input v-model="row.key" :disabled="form.details.rows.some(r=>r.groupKey===row.key)" /></template></el-table-column>
            <el-table-column label="名称" width="150"><template #default="{row}"><el-input v-model="row.name" /></template></el-table-column>
            <el-table-column label="适用条件"><template #default="{row}"><AttributeConditions v-model="row.conditions" :fields="fields" /></template></el-table-column>
            <el-table-column label="必须命中" width="100"><template #default="{row}"><el-checkbox v-model="row.required" /></template></el-table-column>
            <el-table-column width="75"><template #default="{row}"><el-button link type="danger" @click="removeGroup(row)">删除</el-button></template></el-table-column>
          </el-table>
        </el-collapse-item></el-collapse>
        <div class="toolbar body-tools">
          <el-button @click="addMenu=!addMenu">新增行 ▾</el-button>
          <template v-if="addMenu"><el-button @click="addRow(false)">新增行</el-button><el-button @click="addRow(true)">插入行</el-button></template>
          <el-button :disabled="!selected" @click="duplicate">复制选中行</el-button><el-button type="danger" plain :disabled="!selected" @click="removeRow">删除行</el-button>
          <span class="hint">点击行号或规格维护价格性质、包含项、不良扣价和确认信息。</span>
        </div>
        <el-table :data="form.details.rows" border stripe row-key="rowId" highlight-current-row @current-change="r=>selected=r" class="price-table">
          <el-table-column label="行号" width="65"><template #default="{row,$index}"><el-button link type="primary" @click="editRow(row)">{{$index+1}}</el-button></template></el-table-column>
          <el-table-column label="品名规格" min-width="175"><template #default="{row}"><el-button link type="primary" @click="editRow(row)">{{row.label||'未填写规格（点击编辑）'}}</el-button><div class="hint">{{kinds[row.kind]}} · {{row.confirmed&&row.amount!=null?'已确认':'待确认 / 未定价'}}</div></template></el-table-column>
          <el-table-column label="取价条件" min-width="230"><template #default="{row}"><el-button link type="primary" @click="editConditions(row)">{{summary(row.conditions)}}</el-button></template></el-table-column>
          <el-table-column label="属性区间" min-width="155"><template #default="{row}">{{summary(row.conditions.filter(c=>c.operator==='range'))==='不限'?'—':summary(row.conditions.filter(c=>c.operator==='range'))}}</template></el-table-column>
          <el-table-column label="单价" width="145"><template #default="{row}"><el-input-number v-model="row.amount" :controls="false" :precision="4" :min="0" :max="1000000" style="width:125px" /></template></el-table-column>
          <el-table-column label="单位" width="80"><template #default>{{form.priceUnit||'未设'}}</template></el-table-column>
          <el-table-column label="收费组" min-width="150"><template #default="{row}"><el-select v-if="row.kind==='extra'" v-model="row.groupKey" placeholder="请选择"><el-option v-for="g in form.details.groups" :key="g.key" :value="g.key" :label="g.name" /></el-select><span v-else>—</span></template></el-table-column>
        </el-table>
      </el-form>
      <p class="hint">不完整草稿可保存；完整草稿保存即启用，启用失败保留原状态。停用表保存仍停用。未确认行请打开行详情补齐；原始分档价仅作备注，不自动参与匹配。</p>
      <h3>取价试算</h3>
      <p class="hint">使用已保存的有效表；未保存修改不参与试算。</p>
      <div class="toolbar"><el-select v-model="previewProduct" filterable placeholder="试算产品" style="width:300px"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select><el-button :disabled="!id||dirty()" :loading="previewing" @click="preview">试算</el-button></div>
      <el-form label-width="130px"><el-form-item v-for="f in craftFields" :key="f.id" :label="f.fieldName"><el-select v-if="f.fieldType==='single'" v-model="previewAttributes[f.id]" clearable><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select><el-input v-else v-model="previewAttributes[f.id]" placeholder="实际值，未知留空" /></el-form-item></el-form>
      <el-alert v-if="previewError" :title="previewError" type="error" :closable="false" />
      <template v-if="quote"><p><strong>{{quote.baseName}}：{{quote.unitPrice}}元/{{quote.priceUnit}}</strong>；每{{quote.priceUnit}}对应{{quote.unitsPerPriceUnit}}个报工单位</p><el-table :data="quote.attributeEvidence?.rows" border><el-table-column prop="rowId" label="价格行" /><el-table-column prop="reason" label="结果及原因" /><el-table-column prop="amount" label="原价" /><el-table-column prop="source" label="来源" /></el-table></template>
    </template>
    <el-dialog v-model="conditionDialog" title="取价条件（全部同时满足）" width="850px" :close-on-click-modal="false">
      <el-checkbox v-model="unlimited" :disabled="readonly">不限属性条件</el-checkbox>
      <AttributeConditions v-if="!unlimited" v-model="conditionDraft" :fields="fields" :disabled="readonly" />
      <template #footer><el-button @click="conditionDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitConditions">保存条件</el-button></template>
    </el-dialog>
    <el-dialog v-model="rowDialog" title="行详情" width="650px" :close-on-click-modal="false">
      <el-form v-if="rowDraft" :disabled="readonly" label-width="115px">
        <el-form-item label="行标识">{{rowDraft.rowId}}</el-form-item><el-form-item label="品名规格"><el-input v-model="rowDraft.label" maxlength="256" /></el-form-item>
        <el-form-item label="价格性质"><el-select v-model="rowDraft.kind"><el-option v-for="(v,k) in kinds" :key="k" :value="k" :label="v" /></el-select></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="不良扣价"><el-input-number v-model="rowDraft.deductPrice" :min="0" :precision="4" :max="1000000" /></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="已包含附加项"><el-select v-model="rowDraft.includedRowIds" multiple><el-option v-for="r in form.details.rows.filter(r=>r.kind==='extra'&&r.rowId!==rowDraft.rowId)" :key="r.rowId" :value="r.rowId" :label="r.label||r.rowId" /></el-select></el-form-item>
        <el-form-item label="已确认"><el-checkbox v-model="rowDraft.confirmed">价格及收费口径已确认</el-checkbox></el-form-item>
        <el-form-item label="来源" required><el-input v-model="rowDraft.source" placeholder="原表工作表及行号，或人工维护依据" /></el-form-item><el-form-item label="原始备注"><el-input v-model="rowDraft.note" type="textarea" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="rowDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitRow">保存行详情</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getOperationList,getUnitList,getProductList } from '@/api/base'
import { getCustomFields } from '@/api/prod'
import { queryPriceLists,getPriceList,savePriceList,previewAttributePrice,changePriceListStatus,deletePriceList } from '@/api/operationPriceList'
import AttributeConditions from '@/components/AttributeConditions.vue'
const statuses={0:'草稿',1:'启用',2:'停用'},kinds={base:'基础价',extra:'附加价',final:'最终价'},pricingTypes={1:'计件',2:'计时',3:'单笔固定'}
const list=ref([]),total=ref(0),filters=ref({page:1}),operations=ref([]),units=ref([]),products=ref([]),allFields=ref([])
const editing=ref(false),readonly=ref(false),loading=ref(false),saving=ref(false),id=ref(null),form=ref({}),baseline=ref(''),previewProduct=ref(null),previewAttributes=ref({}),previewing=ref(false),previewError=ref(''),quote=ref(null)
const selected=ref(null),addMenu=ref(false),expanded=ref([]),conditionDialog=ref(false),conditionDraft=ref([]),conditionRow=ref(null),unlimited=ref(false),rowDialog=ref(false),rowDraft=ref(null),rowOriginal=ref(null)
const fields=computed(()=>allFields.value.filter(f=>f.target==='product'||f.operationId===form.value.operationId)),craftFields=computed(()=>fields.value.filter(f=>f.target==='work_order_task'))
const day=s=>s?.slice(0,10),clone=v=>JSON.parse(JSON.stringify(v)),dirty=()=>editing.value&&!readonly.value&&JSON.stringify(form.value)!==baseline.value
const opName=id=>operations.value.find(o=>o.id===id)?.name||`工序 ${id}`
const productName=id=>id?((p=>p?`${p.code} ${p.name}`:`产品 ${id}`)(products.value.find(p=>p.id===id))):'通用表（不限产品）'
onMounted(async()=>{loading.value=true;try{const [o,u,p,f,c]=await Promise.all([getOperationList({pageSize:1000}),getUnitList(),getProductList({pageSize:1000}),getCustomFields('product'),getCustomFields('work_order_task')]);operations.value=o.data.list;units.value=u.data;products.value=p.data.list;allFields.value=[...f.data,...c.data];await load()}finally{loading.value=false}})
async function load(){const r=await queryPriceLists(filters.value);list.value=r.data.list;total.value=r.data.total}
async function search(){filters.value.page=1;await load()}
async function reset(){filters.value={page:1};await load()}
function create(){id.value=null;form.value={code:'',name:'',operationId:null,pricingType:1,productId:null,remark:'',activatedAt:null,status:0,effectiveFrom:new Date().toLocaleDateString('sv-SE'),effectiveTo:null,priceUnit:'',reportUnitId:null,unitsPerPriceUnit:1,expectedVersion:'',details:{schemaVersion:2,requiredFieldIds:[],groups:[],rows:[]}};start(false)}
function start(read){readonly.value=read;editing.value=true;selected.value=null;addMenu.value=false;quote.value=null;previewError.value='';previewAttributes.value={};baseline.value=JSON.stringify(form.value)}
async function open(key,read){loading.value=true;try{const r=await getPriceList(key);id.value=key;form.value={...r.data,effectiveFrom:day(r.data.effectiveFrom),effectiveTo:day(r.data.effectiveTo),expectedVersion:r.data.version};start(read)}finally{loading.value=false}}
async function copy(row){await open(row.id,false);id.value=null;form.value.code='';form.value.name+='（复制）';form.value.status=0;form.value.activatedAt=null;form.value.expectedVersion='';baseline.value=JSON.stringify(form.value)}
async function confirmLeave(){if(!dirty())return true;try{await ElMessageBox.confirm('有未保存的修改，确认离开？','提示');return true}catch(action){if(action==='cancel'||action==='close')return false;throw action}}
async function back(){if(await confirmLeave()){editing.value=false;await load()}}
onBeforeRouteLeave(async()=>saving.value?false:await confirmLeave())
watch([()=>form.value.operationId,previewProduct,previewAttributes],()=>{quote.value=null;previewError.value=''},{deep:true})
function operationChanged(){form.value.details.groups=[];form.value.details.requiredFieldIds=[];for(const r of form.value.details.rows){r.conditions=[];r.groupKey=null;r.includedRowIds=[];r.confirmed=false}previewAttributes.value={};ElMessage.info('工序已更换，条件和收费组已清空，请重新配置')}
function pricingTypeChanged(){if(form.value.pricingType===2){form.value.priceUnit='小时';form.value.unitsPerPriceUnit=1}else if(form.value.pricingType===3){form.value.priceUnit='笔';form.value.unitsPerPriceUnit=1}else if(form.value.priceUnit==='小时'||form.value.priceUnit==='笔'){form.value.priceUnit=''}
  if(form.value.pricingType!==1){form.value.details.groups=[];for(const r of form.value.details.rows){r.kind='base';r.groupKey=null;r.includedRowIds=[]}}
  ElMessage.info('计价方式已切换；计时/固定只允许唯一主价，不允许收费组和附加行')}
async function save(){saving.value=true;try{const payload=clone(form.value);payload.productId=payload.productId||null;const r=await savePriceList(id.value,payload);const saved=(await getPriceList(r.data)).data;editing.value=false;await load();ElMessage.success(`已保存：${statuses[saved.status]}`)}finally{saving.value=false}}
async function setStatus(status){saving.value=true;try{await changePriceListStatus(id.value,status,form.value.version);await open(id.value,readonly.value);ElMessage.success(status===1?'已启用':'已停用')}finally{saving.value=false}}
async function removeTable(row){try{await ElMessageBox.confirm(`删除未启用草稿 ${row.code}？`,'删除价目表')}catch(a){if(a==='cancel'||a==='close')return;throw a}await deletePriceList(row.id,row.version);await load();ElMessage.success('已删除')}
function addGroup(){let key;do{key=`g${crypto.randomUUID().slice(0,8)}`}while(form.value.details.groups.some(g=>g.key===key));form.value.details.groups.push({key,name:'',required:true,conditions:[]})}
function removeGroup(g){if(form.value.details.rows.some(r=>r.groupKey===g.key)){ElMessage.error('收费组被价格行引用，请先处理价格行');return}form.value.details.groups.splice(form.value.details.groups.indexOf(g),1)}
function addRow(insert){const r={rowId:crypto.randomUUID(),label:'',kind:'base',conditions:[],amount:null,deductPrice:0,groupKey:null,includedRowIds:[],confirmed:false,source:'',note:''};const i=selected.value?form.value.details.rows.indexOf(selected.value):-1;form.value.details.rows.splice(insert&&i>=0?i:form.value.details.rows.length,0,r);selected.value=r;addMenu.value=false}
function duplicate(){const r=clone(selected.value);r.rowId=crypto.randomUUID();form.value.details.rows.push(r);selected.value=r}
function removeRow(){const row=selected.value;if(form.value.details.rows.some(r=>r.includedRowIds.includes(row.rowId))){ElMessage.error('此行被包含项引用，请先解除引用');return}form.value.details.rows.splice(form.value.details.rows.indexOf(row),1);selected.value=null}
function editRow(row){rowOriginal.value=row;rowDraft.value=clone(row);rowDialog.value=true}
function commitRow(){const r=rowDraft.value;if(r.kind!=='extra')r.groupKey=null;else{r.deductPrice=0;r.includedRowIds=[]}if(r.kind!=='extra'&&form.value.details.rows.some(x=>x.includedRowIds.includes(r.rowId))){ElMessage.error('此行被包含项引用，不能改为非附加价');return}Object.assign(rowOriginal.value,r);rowDialog.value=false}
function editConditions(row){conditionRow.value=row;conditionDraft.value=clone(row.conditions);unlimited.value=!row.conditions.length;conditionDialog.value=true}
function commitConditions(){if(!unlimited.value&&(!conditionDraft.value.length||conditionDraft.value.some(c=>!c.fieldId||c.operator==='eq'&&(c.value==null||c.value==='')||c.operator==='range'&&c.min==null&&c.max==null))){ElMessage.error('请补齐条件，或明确选择不限');return}conditionRow.value.conditions=unlimited.value?[]:clone(conditionDraft.value);conditionDialog.value=false}
function summary(cs){return cs.map(c=>`${fields.value.find(f=>f.id===c.fieldId)?.fieldName||'未知字段'} ${c.operator==='eq'?'='+c.value:`${c.includeMin?'[':'('}${c.min??'不限'},${c.max??'不限'}${c.includeMax?']':')'}`}`).join(' · ')||'不限'}
async function preview(){quote.value=null;previewError.value='';if(!previewProduct.value||!form.value.operationId){ElMessage.error('请选择产品和工序');return}previewing.value=true;try{quote.value=(await previewAttributePrice({productId:previewProduct.value,operationId:form.value.operationId,operationAttributes:previewAttributes.value})).data}catch(e){previewError.value=e.message||'试算失败'}finally{previewing.value=false}}
</script>
<style scoped>
.toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:16px 0}.toolbar h2{flex:1;margin:0}h2 small{font-size:14px;color:#606266}.header-form{display:grid;grid-template-columns:repeat(3,minmax(230px,1fr));margin-top:20px;gap:0 16px}.wide{grid-column:span 2}.hint,.legacy{font-size:12px;color:#606266;line-height:1.8}.legacy{padding:10px}.header-form .el-select{width:100%}.el-pagination{margin-top:16px}.body-tools{padding-top:12px}@media(max-width:1100px){.header-form{grid-template-columns:repeat(2,minmax(230px,1fr))}}
</style>
