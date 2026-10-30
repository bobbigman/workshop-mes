<template>
  <template v-if="fields.length">
    <el-divider content-position="left">规格属性</el-divider>
    <p class="hint">填写产品实际规格；不确定请留空，填0或选否代表这项已确认；修改只影响新建工单。</p>
    <el-form-item v-for="f in fields" :key="f.id" :label="f.fieldName">
      <el-select v-if="f.fieldType==='single'" :model-value="modelValue?.[f.id]" clearable placeholder="未确定" @update:model-value="v=>set(f.id,v)"><el-option v-for="v in f.options" :key="v" :value="v" :label="v" /></el-select>
      <el-input-number v-else-if="f.fieldType==='number'" :model-value="number(modelValue?.[f.id])" :min="0" :controls="false" placeholder="未确定" @update:model-value="v=>set(f.id,v)" />
      <el-input v-else :model-value="modelValue?.[f.id]" clearable @update:model-value="v=>set(f.id,v)" />
    </el-form-item>
  </template>
</template>
<script setup>
const props=defineProps({modelValue:Object,fields:{type:Array,default:()=>[]}}),emit=defineEmits(['update:modelValue'])
const number=v=>v==null||v===''?undefined:Number(v)
function set(id,value){emit('update:modelValue',{...props.modelValue,[id]:value==null||value===''?null:String(value)})}
</script>
<style scoped>.hint{font-size:13px;color:#606266;line-height:1.7}</style>
