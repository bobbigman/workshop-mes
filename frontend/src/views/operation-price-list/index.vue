<template>
  <div v-loading="loading">
    <PriceChangeDialog v-model="adjustVisible" :table-id="adjustId" @done="adjusted" />
    <PriceChangeHistory v-model="historyVisible" :table-id="historyId" />
    <template v-if="!editing">
      <h2>工序价目表</h2>
      <div class="toolbar">
        <el-input v-model="filters.keyword" clearable placeholder="例：JG2024001 / 车削工序（填编号或名称都能搜）" style="width:190px" @keyup.enter="search" />
        <el-select v-model="filters.operationId" clearable placeholder="全部工序" style="width:150px"><el-option v-for="o in operations" :key="o.id" :label="o.name" :value="o.id" /></el-select>
        <el-select v-model="filters.status" clearable placeholder="全部状态" style="width:125px"><el-option v-for="(v,k) in statuses" :key="k" :label="v" :value="Number(k)" /></el-select>
        <el-date-picker v-model="filters.date" value-format="YYYY-MM-DD" placeholder="选择日期查当天有效的价目表" style="width:160px" />
        <el-button @click="search">查询</el-button><el-button @click="reset">重置</el-button><el-button @click="load">刷新</el-button>
        <el-button type="primary" @click="create">＋ 新增价目表</el-button>
      </div>
      <mes-table table-key="views/operation-price-list/index:1" :data="list" border stripe>
        <el-table-column label="单据编号" min-width="150"><template #default="{row}"><el-button link type="primary" @click="open(row.id,true)">{{row.code}}</el-button></template></el-table-column>
        <el-table-column prop="name" label="名称" min-width="150" />
        <el-table-column label="价目表类型 / 工序" min-width="150"><template #default="{row}">{{opName(row.operationId)}}</template></el-table-column>
        <el-table-column label="工价计算方式" width="95"><template #default="{row}">{{pricingTypes[row.pricingType]}}</template></el-table-column>
        <el-table-column label="产品范围" min-width="180"><template #default="{row}">{{productName(row.productId)}}</template></el-table-column>
        <el-table-column label="明细行数" width="95"><template #default="{row}">{{row.details.rows.length}}</template></el-table-column>
        <el-table-column label="状态" width="95"><template #default="{row}"><el-tag :type="row.status===1?'success':row.status===2?'info':'warning'">{{statuses[row.status]}}</el-tag></template></el-table-column>
        <el-table-column label="生效 ~ 失效" min-width="205"><template #default="{row}">{{day(row.effectiveFrom)}} ~ {{row.effectiveTo?day(row.effectiveTo):'长期'}}</template></el-table-column>
        <el-table-column label="更新时间" min-width="160"><template #default="{row}">{{row.updatedAt?.replace('T',' ').slice(0,19)}}</template></el-table-column>
        <el-table-column label="操作" width="330" fixed="right"><template #default="{row}"><el-button link type="primary" @click="open(row.id,false)">编辑</el-button><el-button v-if="row.status===1" link type="primary" @click="adjustId=row.id;adjustVisible=true">调整工价</el-button><el-button link @click="historyId=row.id;historyVisible=true">调价记录</el-button><el-button link @click="copy(row)">复制</el-button><el-button link type="danger" :disabled="row.status!==0||!!row.activatedAt" @click="removeTable(row)">删除</el-button></template></el-table-column>
      </mes-table>
      <el-pagination v-model:current-page="filters.page" :page-size="20" :total="total" layout="prev,pager,next,total" @current-change="load" />
    </template>
    <template v-else>
      <div class="toolbar">
        <h2>工序价目表 <small>{{readonly?'浏览':id?'编辑':'新增'}}</small></h2>
        <el-button v-if="readonly" @click="readonly=false">编辑</el-button>
        <el-button v-if="!readonly" type="primary" :loading="saving" @click="save">保存</el-button>
        <el-button v-if="id&&form.status===1" :disabled="saving||dirty()" @click="adjustId=id;adjustVisible=true">调整工价</el-button>
        <el-button v-if="id&&form.status===1" :disabled="saving||dirty()" @click="setStatus(2)">停用</el-button>
        <el-button v-if="id&&form.status===2" :disabled="saving||dirty()" @click="setStatus(1)">启用</el-button>
        <el-button :disabled="saving" @click="back">返回列表</el-button>
      </div>
      <el-alert v-if="readonly" class="browse-notice" title="当前是浏览模式，可复制内容；要修改请点上方的“编辑”按钮" type="info" show-icon :closable="false" />
      <el-alert title="系统优先使用产品专属价目表；没有有效的专属表时才用通用表；专属表里缺少的规格不会自动从通用表补" type="info" :closable="false" />
      <el-form v-if="readonly" label-width="110px" class="header-form browse-content">
        <el-form-item v-for="item in browseHeader" :key="item.label" :label="item.label" :class="{wide:item.label==='备注'}"><span class="copyable">{{item.value??'—'}}</span></el-form-item>
      </el-form>
      <el-form v-else :disabled="saving" label-width="110px" class="header-form">
        <el-form-item label="单据编号" required><el-input v-model="form.code" :disabled="!!id" maxlength="64" /></el-form-item>
        <el-form-item label="名称" required><el-input v-model="form.name" maxlength="128" /></el-form-item>
        <el-form-item label="价目表类型" required><el-select v-model="form.operationId" :disabled="!!form.activatedAt" filterable @change="operationChanged"><el-option v-for="o in operations" :key="o.id" :value="o.id" :label="o.name" /></el-select><span v-if="form.activatedAt" class="hint">价目表首次启用后，所属工序不可更改；如需更换工序，请新建一张价目表</span></el-form-item>
        <el-form-item label="工价计算方式" required><el-select v-model="form.pricingType" :disabled="!!form.activatedAt" @change="pricingTypeChanged"><el-option v-for="(v,k) in pricingTypes" :key="Number(k)" :value="Number(k)" :label="v" /></el-select><span v-if="form.activatedAt" class="hint">价目表首次启用后，工价计算方式不可更改；如需更换，请新建一张价目表</span></el-form-item>
        <el-form-item label="产品"><el-select v-model="form.productId" filterable clearable placeholder="通用表（不限产品）"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select></el-form-item>
        <el-form-item label="生效日期" required><el-date-picker v-model="form.effectiveFrom" value-format="YYYY-MM-DD" /></el-form-item>
        <el-form-item label="失效日期"><el-date-picker v-model="form.effectiveTo" value-format="YYYY-MM-DD" clearable /></el-form-item>
        <el-form-item label="计价单位" required><el-input v-model="form.priceUnit" placeholder="例：打、件、小时、公斤" maxlength="32" /></el-form-item>
        <el-form-item label="报工单位" required><el-select v-model="form.reportUnitId"><el-option v-for="u in units" :key="u.id" :value="u.id" :label="u.name" /></el-select></el-form-item>
        <el-form-item label="折算比例" required><el-input-number v-model="form.unitsPerPriceUnit" :precision="4" :min="0.0001" :max="1000000" /><span class="hint">每计价单位对应报工数</span></el-form-item>
        <el-form-item label="状态"><el-tag>{{statuses[form.status]}}</el-tag></el-form-item>
        <el-form-item label="备注" class="wide"><el-input v-model="form.remark" maxlength="1000" placeholder="例：本表适用于某产品线所有规格，按计件计价" /></el-form-item>
      </el-form>
      <el-form :disabled="readonly||saving">
        <el-collapse v-model="expanded"><el-collapse-item title="收费组说明：同一组内只取一条匹配价格，不同组的费用叠加计算" name="groups">
          <el-button v-if="!readonly" @click="addGroup">新增收费组</el-button>
          <mes-table table-key="views/operation-price-list/index:2" :data="form.details.groups" border>
            <el-table-column label="字段字段编码" width="150"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.key}}</span><el-input v-else v-model="row.key" :disabled="form.details.rows.some(r=>r.groupKey===row.key)" /></template></el-table-column>
            <el-table-column label="名称" width="150"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.name}}</span><el-input v-else v-model="row.name" /></template></el-table-column>
            <el-table-column label="适用条件"><template #default="{row}"><span v-if="readonly" class="copyable">{{summary(row.conditions)}}</span><AttributeConditions v-else v-model="row.conditions" :fields="fields" /></template></el-table-column>
            <el-table-column label="必选条件" width="100"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.required?'是':'否'}}</span><el-checkbox v-else v-model="row.required" /></template></el-table-column>
            <el-table-column v-if="!readonly" width="75"><template #default="{row}"><el-button link type="danger" @click="removeGroup(row)">删除</el-button></template></el-table-column>
          </mes-table>
        </el-collapse-item></el-collapse>
        <div v-if="!readonly" class="toolbar body-tools">
          <el-button @click="addMenu=!addMenu">新增行 ▾</el-button>
          <template v-if="addMenu"><el-button @click="addRow(false)">新增行</el-button><el-button @click="addRow(true)">插入行</el-button></template>
          <el-button :disabled="!selected" @click="duplicate">复制选中行</el-button><el-button type="danger" plain :disabled="!selected" @click="removeRow">删除行</el-button>
          <span class="hint">点击行号或规格可编辑价格性质、包含项和确认信息；已启用的价目表如需改金额或不良扣价，请点“调整工价”</span>
        </div>
        <mes-table table-key="views/operation-price-list/index:3" :data="form.details.rows" border stripe row-key="rowId" highlight-current-row @current-change="r=>selected=r" class="price-table">
          <el-table-column label="行号" width="65"><template #default="{row,$index}"><span v-if="readonly" class="copyable">{{$index+1}}</span><el-button v-else link type="primary" @click="editRow(row)">{{$index+1}}</el-button></template></el-table-column>
          <el-table-column label="品名规格" min-width="175"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.label||'暂无规格'}}</span><el-button v-else link type="primary" @click="editRow(row)">{{row.label||'暂无规格，点击补充'}}</el-button><div class="hint">{{kinds[row.kind]}} · {{row.confirmed&&row.amount!=null?'已确认':'待确认 / 未定价'}}</div></template></el-table-column>
          <el-table-column label="取价条件" min-width="230"><template #default="{row}"><span v-if="readonly" class="copyable">{{summary(row.conditions)}}</span><el-button v-else link type="primary" @click="editConditions(row)">{{summary(row.conditions)}}</el-button></template></el-table-column>
          <el-table-column label="属性区间" min-width="155"><template #default="{row}">{{summary(row.conditions.filter(c=>c.operator==='range'))==='不限'?'—':summary(row.conditions.filter(c=>c.operator==='range'))}}</template></el-table-column>
          <el-table-column label="单价" width="145"><template #default="{row}"><span v-if="readonly" class="copyable">{{row.amount??'未定价'}}</span><el-input-number v-else v-model="row.amount" :disabled="priceFrozen(row.rowId)" :controls="false" :precision="4" :min="0" :max="1000000" style="width:125px" /></template></el-table-column>
          <el-table-column label="单位" width="80"><template #default>{{form.priceUnit||'未设'}}</template></el-table-column>
          <el-table-column label="收费组" min-width="150"><template #default="{row}"><span v-if="readonly&&row.kind==='extra'" class="copyable">{{form.details.groups.find(g=>g.key===row.groupKey)?.name||row.groupKey||'未选择'}}</span><el-select v-else-if="row.kind==='extra'" v-model="row.groupKey" placeholder="请选择"><el-option v-for="g in form.details.groups" :key="g.key" :value="g.key" :label="g.name" /></el-select><span v-else>—</span></template></el-table-column>
        </mes-table>
      </el-form>
      <p class="hint">不完整的草稿可以先保存；完整草稿保存后自动启用，启用失败不影响原来的状态；已停用的表保存后仍保持停用；未确认的行请打开行详情补齐信息；原始分档价只作备注，不参与自动取价</p>
      <h3>取价试算</h3>
      <p class="hint">试算基于已保存的有效价目表，当前未保存的修改不参与试算</p>
      <div class="toolbar"><el-select v-model="previewProduct" filterable placeholder="选择一个产品进行试算" style="width:300px"><el-option v-for="p in products" :key="p.id" :value="p.id" :label="`${p.code} ${p.name}`" /></el-select><el-button :disabled="!id||dirty()" :loading="previewing" @click="preview">试算</el-button></div>
      <el-form label-width="130px"><el-form-item v-for="f in craftFields" :key="f.id" :label="f.fieldName"><el-select v-if="f.fieldType==='single'" v-model="previewAttributes[f.id]" clearable><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select><el-input v-else v-model="previewAttributes[f.id]" placeholder="填入实际值，不清楚的字段留空即可" /></el-form-item></el-form>
      <el-alert v-if="previewError" :title="previewError" type="error" :closable="false" />
      <template v-if="quote"><p><strong>{{quote.baseName}}：{{quote.unitPrice}}元/{{quote.priceUnit}}</strong>；每{{quote.priceUnit}}对应{{quote.unitsPerPriceUnit}}个报工单位</p><mes-table table-key="views/operation-price-list/index:4" :data="quote.attributeEvidence?.rows" border><el-table-column prop="rowId" label="价格行" /><el-table-column prop="reason" label="结果及原因" /><el-table-column prop="amount" label="原价" /><el-table-column prop="source" label="数据数据来源" /></mes-table></template>
    </template>
    <el-dialog v-model="conditionDialog" title="取价条件（全部同时满足）" width="850px" :close-on-click-modal="false">
      <el-checkbox v-model="unlimited" :disabled="readonly">不限制属性条件（任何属性都匹配）</el-checkbox>
      <AttributeConditions v-if="!unlimited" v-model="conditionDraft" :fields="fields" :disabled="readonly" />
      <template #footer><el-button @click="conditionDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitConditions">保存条件</el-button></template>
    </el-dialog>
    <el-dialog v-model="rowDialog" title="行详情" width="650px" :close-on-click-modal="false">
      <el-form v-if="rowDraft" :disabled="readonly" label-width="115px">
        <el-form-item label="行标识">{{rowDraft.rowId}}</el-form-item><el-form-item label="品名规格"><el-input v-model="rowDraft.label" maxlength="256" /></el-form-item>
        <el-form-item label="价格性质"><el-select v-model="rowDraft.kind"><el-option v-for="(v,k) in kinds" :key="k" :value="k" :label="v" /></el-select></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="不良扣价"><el-input-number v-model="rowDraft.deductPrice" :disabled="priceFrozen(rowDraft.rowId)" :min="0" :precision="4" :max="1000000" /></el-form-item>
        <el-form-item v-if="rowDraft.kind!=='extra'" label="已包含附加项"><el-select v-model="rowDraft.includedRowIds" multiple><el-option v-for="r in form.details.rows.filter(r=>r.kind==='extra'&&r.rowId!==rowDraft.rowId)" :key="r.rowId" :value="r.rowId" :label="r.label||r.rowId" /></el-select></el-form-item>
        <el-form-item label="已确认"><el-checkbox v-model="rowDraft.confirmed">我已确认该行的价格和收费口径无误</el-checkbox></el-form-item>
        <el-form-item label="来源" required><el-input v-model="rowDraft.source" placeholder="例：Excel报价表Sheet1第3行，或手工录入依据" /></el-form-item><el-form-item label="原始备注"><el-input v-model="rowDraft.note" type="textarea" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="rowDialog=false">取消</el-button><el-button v-if="!readonly" type="primary" @click="commitRow">保存行详情</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import PriceChangeDialog from './PriceChangeDialog.vue'
import PriceChangeHistory from './PriceChangeHistory.vue'
const historyVisible=ref(false),historyId=ref(null)
const adjustVisible=ref(false), adjustId=ref(null)
async function adjusted(){await load();if(editing.value&&id.value) await open(id.value,true)}
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
const priceFrozen=rowId=>!!form.value.activatedAt&&!!baseline.value&&JSON.parse(baseline.value).details.rows.some(r=>r.rowId===rowId)
const opName=id=>operations.value.find(o=>o.id===id)?.name||`未知工序（编号 ${id}）`
const productName=id=>id?((p=>p?`${p.code} ${p.name}`:`未知产品（编号 ${id}）`)(products.value.find(p=>p.id===id))):'通用表（不限产品）'
const browseHeader=computed(()=>[
  {label:'单据编号',value:form.value.code},{label:'名称',value:form.value.name},
  {label:'价目表类型',value:opName(form.value.operationId)},{label:'计价方式',value:pricingTypes[form.value.pricingType]},
  {label:'产品',value:productName(form.value.productId)},{label:'生效日期',value:form.value.effectiveFrom},
  {label:'失效日期',value:form.value.effectiveTo||'长期'},{label:'计价单位',value:form.value.priceUnit},
  {label:'报工单位',value:units.value.find(u=>u.id===form.value.reportUnitId)?.name||form.value.reportUnitId},
  {label:'折算比例',value:form.value.unitsPerPriceUnit},{label:'状态',value:statuses[form.value.status]},
  {label:'备注',value:form.value.remark||'—'}
])
onMounted(async()=>{loading.value=true;try{const [o,u,p,f,c]=await Promise.all([getOperationList({pageSize:1000}),getUnitList(),getProductList({pageSize:1000}),getCustomFields('product'),getCustomFields('work_order_task')]);operations.value=o.data.list;units.value=u.data;products.value=p.data.list;allFields.value=[...f.data,...c.data];await load()}finally{loading.value=false}})
async function load(){const r=await queryPriceLists(filters.value);list.value=r.data.list;total.value=r.data.total}
async function search(){filters.value.page=1;await load()}
async function reset(){filters.value={page:1};await load()}
function create(){id.value=null;form.value={code:'',name:'',operationId:null,pricingType:1,productId:null,remark:'',activatedAt:null,status:0,effectiveFrom:new Date().toLocaleDateString('sv-SE'),effectiveTo:null,priceUnit:'',reportUnitId:null,unitsPerPriceUnit:1,expectedVersion:'',details:{schemaVersion:2,requiredFieldIds:[],groups:[],rows:[]}};start(false)}
function start(read){readonly.value=read;editing.value=true;selected.value=null;addMenu.value=false;quote.value=null;previewError.value='';previewAttributes.value={};baseline.value=JSON.stringify(form.value)}
async function open(key,read){loading.value=true;try{const r=await getPriceList(key);id.value=key;form.value={...r.data,effectiveFrom:day(r.data.effectiveFrom),effectiveTo:day(r.data.effectiveTo),expectedVersion:r.data.version};start(read)}finally{loading.value=false}}
async function copy(row){await open(row.id,false);id.value=null;form.value.code='';form.value.name+='（复制）';form.value.status=0;form.value.activatedAt=null;form.value.expectedVersion='';baseline.value=JSON.stringify(form.value)}
async function confirmLeave(){if(!dirty())return true;try{await ElMessageBox.confirm('有未保存的修改，离开后这些修改会丢失。确认离开吗？','提示');return true}catch(action){if(action==='cancel'||action==='close')return false;throw action}}
async function back(){if(await confirmLeave()){editing.value=false;await load()}}
onBeforeRouteLeave(async()=>saving.value?false:await confirmLeave())
watch([()=>form.value.operationId,previewProduct,previewAttributes],()=>{quote.value=null;previewError.value=''},{deep:true})
function operationChanged(){form.value.details.groups=[];form.value.details.requiredFieldIds=[];for(const r of form.value.details.rows){r.conditions=[];r.groupKey=null;r.includedRowIds=[];r.confirmed=false}previewAttributes.value={};ElMessage.info('工序已更换，之前的条件和收费组已清空，请在下方重新设置适用条件和收费组')}
function pricingTypeChanged(){if(form.value.pricingType===2){form.value.priceUnit='小时';form.value.unitsPerPriceUnit=1}else if(form.value.pricingType===3){form.value.priceUnit='笔';form.value.unitsPerPriceUnit=1}else if(form.value.priceUnit==='小时'||form.value.priceUnit==='笔'){form.value.priceUnit=''}
  if(form.value.pricingType!==1){form.value.details.groups=[];for(const r of form.value.details.rows){r.kind='base';r.groupKey=null;r.includedRowIds=[]}}
  ElMessage.info('工价计算方式已切换；计时和单笔固定只允许一个主价行，不支持收费组和附加行，请检查并调整价格行')}
async function save(){saving.value=true;try{const payload=clone(form.value);payload.productId=payload.productId||null;const r=await savePriceList(id.value,payload);const saved=(await getPriceList(r.data)).data;editing.value=false;await load();ElMessage.success(`已保存，当前状态：${statuses[saved.status]}`)}finally{saving.value=false}}
async function setStatus(status){saving.value=true;try{await changePriceListStatus(id.value,status,form.value.version);await open(id.value,readonly.value);ElMessage.success(status===1?'已启用':'已停用')}finally{saving.value=false}}
async function removeTable(row){try{await ElMessageBox.confirm(`确定删除草稿价目表 ${row.code} 吗？删除后无法恢复`,'删除价目表')}catch(a){if(a==='cancel'||a==='close')return;throw a}await deletePriceList(row.id,row.version);await load();ElMessage.success('已删除成功成功')}
function addGroup(){let key;do{key=`g${crypto.randomUUID().slice(0,8)}`}while(form.value.details.groups.some(g=>g.key===key));form.value.details.groups.push({key,name:'',required:true,conditions:[]})}
function removeGroup(g){if(form.value.details.rows.some(r=>r.groupKey===g.key)){ElMessage.error('该收费组正在被价格行使用，无法删除；请先在下方表格中移除或修改引用它的价格行，再删除收费组');return}form.value.details.groups.splice(form.value.details.groups.indexOf(g),1)}
function addRow(insert){const r={rowId:crypto.randomUUID(),label:'',kind:'base',conditions:[],amount:null,deductPrice:0,groupKey:null,includedRowIds:[],confirmed:false,source:'',note:''};const i=selected.value?form.value.details.rows.indexOf(selected.value):-1;form.value.details.rows.splice(insert&&i>=0?i:form.value.details.rows.length,0,r);selected.value=r;addMenu.value=false}
function duplicate(){const r=clone(selected.value);r.rowId=crypto.randomUUID();form.value.details.rows.push(r);selected.value=r}
function removeRow(){const row=selected.value;if(form.value.details.rows.some(r=>r.includedRowIds.includes(row.rowId))){ElMessage.error('该行被其他行的“已包含附加项”引用，无法删除；请先打开引用它的行，取消勾选后再删除');return}form.value.details.rows.splice(form.value.details.rows.indexOf(row),1);selected.value=null}
function editRow(row){rowOriginal.value=row;rowDraft.value=clone(row);rowDialog.value=true}
function commitRow(){const r=rowDraft.value;if(r.kind!=='extra')r.groupKey=null;else{r.deductPrice=0;r.includedRowIds=[]}if(r.kind!=='extra'&&form.value.details.rows.some(x=>x.includedRowIds.includes(r.rowId))){ElMessage.error('该行已被其他行作为附加项引用，不能改为非附加价类型；请先在引用它的行取消勾选，再修改价格性质');return}Object.assign(rowOriginal.value,r);rowDialog.value=false}
function editConditions(row){conditionRow.value=row;conditionDraft.value=clone(row.conditions);unlimited.value=!row.conditions.length;conditionDialog.value=true}
function commitConditions(){if(!unlimited.value&&(!conditionDraft.value.length||conditionDraft.value.some(c=>!c.fieldId||c.operator==='eq'&&(c.value==null||c.value==='')||c.operator==='range'&&c.min==null&&c.max==null))){ElMessage.error('请至少添加一个取价条件并填写完整；如果确实不需要条件限制，请勾选上方的“不限制属性条件（任何属性都匹配）”');return}conditionRow.value.conditions=unlimited.value?[]:clone(conditionDraft.value);conditionDialog.value=false}
function summary(cs){return cs.map(c=>`${fields.value.find(f=>f.id===c.fieldId)?.fieldName||'未知字段'} ${c.operator==='eq'?'='+c.value:`${c.includeMin?'[':'('}${c.min??'不限'},${c.max??'不限'}${c.includeMax?']':')'}`}`).join(' · ')||'不限'}
async function preview(){quote.value=null;previewError.value='';if(!previewProduct.value||!form.value.operationId){ElMessage.error('请先在上方试算区域选择一个产品和工序，再进行试算');return}previewing.value=true;try{quote.value=(await previewAttributePrice({productId:previewProduct.value,operationId:form.value.operationId,operationAttributes:previewAttributes.value})).data}catch(e){previewError.value=e.message||'试算失败，请检查价目表是否已保存并启用，以及试算产品的属性是否填写完整，请检查价目表是否已保存并启用，以及选择一个产品进行试算的属性是否填写完整'}finally{previewing.value=false}}
</script>
<style scoped>
.browse-notice{margin-bottom:12px}.copyable,.browse-content{user-select:text;cursor:text;overflow-wrap:anywhere;white-space:pre-wrap}.price-table :deep(.cell){user-select:text}
.toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:16px 0}.toolbar h2{flex:1;margin:0}h2 small{font-size:14px;color:#606266}.header-form{display:grid;grid-template-columns:repeat(3,minmax(230px,1fr));margin-top:20px;gap:0 16px}.wide{grid-column:span 2}.hint,.legacy{font-size:12px;color:#606266;line-height:1.8}.legacy{padding:10px}.header-form .el-select{width:100%}.el-pagination{margin-top:16px}.body-tools{padding-top:12px}@media(max-width:1100px){.header-form{grid-template-columns:repeat(2,minmax(230px,1fr))}}
</style>
