<template>
  <div class="conditions">
    <span v-if="!modelValue.length" class="muted">不限</span>
    <div v-for="(c,i) in modelValue" :key="i" class="condition">
      <el-select :disabled="disabled" v-model="c.fieldId" placeholder="属性" filterable style="width:150px" @change="()=>reset(c)">
        <el-option v-for="f in fields" :key="f.id" :value="f.id" :label="f.fieldName" />
      </el-select>
      <el-select :disabled="disabled" v-model="c.operator" style="width:95px">
        <el-option value="eq" label="等于" /><el-option v-if="field(c)?.fieldType==='number'" value="range" label="区间" />
      </el-select>
      <template v-if="c.operator==='eq'">
        <el-select :disabled="disabled" v-if="field(c)?.fieldType==='single'" v-model="c.value" style="width:140px"><el-option v-for="v in field(c).options" :key="v" :value="v" :label="v" /></el-select>
        <el-input :disabled="disabled" v-else v-model="c.value" placeholder="例：合格 或 100" style="width:120px" />
      </template>
      <template v-else>
        <el-input-number :disabled="disabled" v-model="c.min" :controls="false" placeholder="下界不限" style="width:110px" />
        <el-checkbox :disabled="disabled" v-model="c.includeMin">含下界</el-checkbox><span>至</span>
        <el-input-number :disabled="disabled" v-model="c.max" :controls="false" placeholder="上界不限" style="width:110px" />
        <el-checkbox :disabled="disabled" v-model="c.includeMax">含上界</el-checkbox>
      </template>
      <el-button :disabled="disabled" link type="danger" @click="remove(i)">移除</el-button>
    </div>
    <el-button :disabled="disabled" link type="primary" @click="add">+ 添加条件（需全部满足）</el-button>
  </div>
</template>
<script setup>
const props=defineProps({disabled:Boolean,modelValue:{type:Array,default:()=>[]},fields:{type:Array,default:()=>[]}})
const emit=defineEmits(['update:modelValue'])
const field=c=>props.fields.find(f=>f.id===c.fieldId)
function reset(c){c.operator='eq';c.value=null;c.min=null;c.max=null}
function add(){emit('update:modelValue',[...props.modelValue,{fieldId:null,operator:'eq',value:null,min:null,max:null,includeMin:true,includeMax:true}])}
function remove(i){emit('update:modelValue',props.modelValue.filter((_,n)=>n!==i))}
</script>
<style scoped>.condition{display:flex;align-items:center;gap:6px;margin:6px 0;flex-wrap:wrap}.muted{color:#909399}</style>
