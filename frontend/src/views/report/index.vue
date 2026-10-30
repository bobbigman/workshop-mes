<template>
  <div>
    <div v-if="managementEntry" class="management-entry">
      <router-link to="/h5/home">‹ 返回工作台</router-link>
      <p v-if="!managementAllowed" role="alert">无权使用该操作或入口无效，请返回工作台。</p>
    </div>
    <template v-else>
    <ValueTip :text="VALUE_TIPS.report" />
    <div class="toolbar">
      <el-select
        v-model="q.orderNo"
        filterable
        clearable
        allow-create
        default-first-option
        placeholder="工单编号"
        style="width:220px"
        @change="load"
        @clear="load"
      >
        <el-option
          v-for="o in orderOptions"
          :key="o.value"
          :label="o.label"
          :value="o.value"
        />
      </el-select>
      <el-input v-model="q.productCode" placeholder="产品编号" clearable style="width:160px" />
      <el-input v-model="q.productName" placeholder="产品名称" clearable style="width:160px" />
      <el-select v-model="q.userType" clearable placeholder="人员类型" style="width:140px" @change="load">
        <el-option v-for="o in USER_TYPE_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
      <el-button type="primary" @click="load">查询</el-button>
      <el-button type="success" @click="openCreate()">+ 报工</el-button>
      <el-button v-if="canProxy" type="primary" plain @click="openProxy()">代报工</el-button>
      <el-button v-if="isAdmin" type="warning" @click="openBatch()">一键补报未完工序</el-button>
      <el-button v-if="isAdmin" @click="openPending">待补报开工记录</el-button>
    </div>
    <mes-table table-key="views/report/index:1" :data="list">
      <el-table-column prop="orderNo" label="工单编号" />
      <el-table-column prop="productName" label="产品" />
      <el-table-column prop="operationName" label="工序" />
      <el-table-column prop="userName" label="报工人" width="90" />
      <el-table-column label="人员类型" width="110">
        <template #default="{ row }">
          <el-tag :type="userTypeTagType(row.userType)" size="small">{{ userTypeLabel(row.userType) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="goodQty" label="良品" width="80" />
      <el-table-column prop="defectQty" label="不良品" width="80" />
      <el-table-column prop="defectName" label="不良原因" />
      <el-table-column label="单价" width="120">
        <template #default="{ row }">
          <template v-if="row.unitPrice != null">¥{{ Number(row.unitPrice).toFixed(4) }}</template>
          <template v-else>
            <span class="wage-miss">未配工价</span>
            <el-button v-if="isAdmin" link type="primary" @click="$router.push('/operation-price-list')">去补价</el-button>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="时长" width="110">
        <template #default="{ row }">{{ formatDuration(row.durationMinutes) }}</template>
      </el-table-column>
      <el-table-column label="复核" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.reviewStatus === 0" type="warning" size="small">待复核</el-tag>
          <el-tag v-else-if="row.reviewStatus === 1" type="success" size="small">已通过</el-tag>
          <el-tag v-else-if="row.reviewStatus === 2" type="danger" size="small">已退回</el-tag>
          <span v-else>-</span>
        </template>
      </el-table-column>
      <el-table-column prop="reportTime" label="时间" width="170" />
      <el-table-column label="操作" width="140">
        <template #default="{ row }">
          <el-button
            link
            type="primary"
            :disabled="row.reviewStatus === 1 || row.settledFlag"
            @click="openEdit(row)"
          >修改</el-button>
          <el-button link type="info" @click="openChangeLogs(row)">修改记录</el-button>
        </template>
      </el-table-column>
    </mes-table>
    </template>

    <!-- 新增报工（电脑端） -->
    <el-dialog v-model="createDlg" title="新增报工" width="560px" @closed="resetCreate">
      <el-form :model="createForm" label-width="90px">
        <el-form-item label="工单号" required>
          <el-select
            v-model="createForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onCreateOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="orderDetail" label="产品">
          <span>{{ orderDetail.productName }}（计划 {{ orderDetail.qty }}）</span>
        </el-form-item>
        <el-form-item v-if="orderDetail" label="工序进度">
          <div class="summary">
            <span>计划 {{ summary.plan }}</span>
            <span>良品 {{ summary.done }}</span>
            <span>不良 {{ summary.defect }}</span>
            <span>剩余 {{ summary.remain }}</span>
          </div>
        </el-form-item>
        <el-form-item label="工序" required>
          <el-select
            v-model="createForm.operationId"
            style="width:100%"
            placeholder="请先加载工单"
            :disabled="!orderDetail"
            @change="onOpChange"
          >
            <el-option
              v-for="o in (orderDetail?.tasks || [])"
              :key="o.operationId"
              :label="`${o.seq} ${o.operationName}`"
              :value="o.operationId"
            />
          </el-select>
        </el-form-item>
        <ProcessingRequirements :price="orderDetail?.tasks?.find(t=>t.operationId===createForm.operationId)?.processingPrice" />
        <el-form-item label="良品数"><el-input-number v-model="createForm.goodQty" :min="0" :max="remainNum" /></el-form-item>
        <el-form-item label="不良品数"><el-input-number v-model="createForm.defectQty" :min="0" /></el-form-item>
        <el-form-item label="不良原因">
          <el-select v-model="createForm.defectId" clearable style="width:100%" placeholder="选择原因" :disabled="!createForm.defectQty">
            <el-option v-for="d in defectOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="报工时长">
          <div class="duration">
            <el-input-number v-model="createForm.hours" :min="0" :max="24" />
            <span>小时</span>
            <el-input-number v-model="createForm.minutes" :min="0" :max="59" />
            <span>分钟</span>
          </div>
        </el-form-item>
        <el-form-item v-if="isAdmin && !shareEnabled" label="计时补报">
          <el-date-picker v-model="createTimes" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss" start-placeholder="实际开始" end-placeholder="实际结束" />
        </el-form-item>
        <el-form-item label="多人报工">
          <el-checkbox v-model="shareEnabled">多人共同报工</el-checkbox>
        </el-form-item>
        <template v-if="shareEnabled">
          <el-form-item label="分摊">
            <el-radio-group v-model="shareMode">
              <el-radio :value="1">平均</el-radio>
              <el-radio :value="2">按工时</el-radio>
            </el-radio-group>
          </el-form-item>
          <el-form-item label="参与人">
            <div class="share-box">
              <div class="hint">须含自己。按工时填的是分钟；选平均则按上面的良品数平分，每人旁边是分到的个数。分不尽的归当前报工人。</div>
              <div v-if="candidatesLoadError" class="hint">
                参与人列表加载失败
                <el-button link type="primary" @click="reloadCreateCandidates">重试</el-button>
              </div>
              <div v-else-if="!createForm.operationId" class="hint">请先选择工序</div>
              <div v-else-if="!createCandidates.length" class="hint">该工序暂无可选参与人</div>
              <el-checkbox-group v-else v-model="participantIds">
                <div v-for="c in createCandidates" :key="c.id" class="share-person">
                  <el-checkbox :value="c.id">{{ c.name }}</el-checkbox>
                  <template v-if="participantIds.map(Number).includes(Number(c.id))">
                    <template v-if="shareMode === 2">
                      <el-input-number v-model="participantDurations[c.id]" :min="0" :max="1440" size="small" class="share-min" />
                      <span class="share-unit">分钟</span>
                    </template>
                    <span v-else class="share-unit">{{ shareGood(c.id) }} 个</span>
                    <el-date-picker v-if="isAdmin" v-model="participantTimes[c.id]" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss" start-placeholder="未开工：补填开始" end-placeholder="补填结束" size="small" style="width:340px" />
                  </template>
                </div>
              </el-checkbox-group>
              <div v-if="sharePreview.length" class="share-preview">
                <div v-for="p in sharePreview" :key="p.userId">{{ p.name }}：良品 {{ p.good }} 个 / 不良 {{ p.defect }} 个</div>
              </div>
            </div>
          </el-form-item>
        </template>
      </el-form>
      <template #footer>
        <el-button @click="createDlg=false">取消</el-button>
        <el-button type="primary" :loading="submitting" :disabled="locked" @click="onCreate(false)">提交</el-button>
        <el-button type="success" :loading="submitting" :disabled="locked" @click="onCreate(true)">继续报工</el-button>
      </template>
    </el-dialog>

    <!-- 修改报工 -->
    <el-dialog v-model="dlg" title="修改报工" width="440px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="工序">
          <el-input :model-value="form.operationName" disabled />
        </el-form-item>
        <el-form-item label="良品数"><el-input-number v-model="form.goodQty" :min="0" /></el-form-item>
        <el-form-item label="不良品数"><el-input-number v-model="form.defectQty" :min="0" /></el-form-item>
        <el-form-item label="不良原因">
          <el-select v-model="form.defectId" clearable style="width:100%" placeholder="选择原因">
            <el-option v-for="d in defectOptions" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="报工时长">
          <div class="duration">
            <el-input-number v-model="form.hours" :min="0" :max="24" />
            <span>小时</span>
            <el-input-number v-model="form.minutes" :min="0" :max="59" />
            <span>分钟</span>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg=false">取消</el-button>
        <el-button type="primary" @click="onSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 修改日志（docs/205） -->
    <el-dialog v-model="logDlg" title="修改记录" width="640px">
      <mes-table table-key="views/report/index:2" :data="changeLogs" v-loading="logLoading" empty-text="暂无修改记录" max-height="420">
        <el-table-column prop="changedAt" label="时间" width="170" />
        <el-table-column prop="changedByName" label="修改人" width="90" />
        <el-table-column label="来源" width="60">
          <template #default="{ row }">{{ row.source === 2 ? 'H5' : 'PC' }}</template>
        </el-table-column>
        <el-table-column label="原值 → 新值" min-width="220">
          <template #default="{ row }">
            良品 {{ row.oldGoodQty }}→{{ row.newGoodQty }}；
            不良 {{ row.oldDefectQty }}→{{ row.newDefectQty }}；
            时长 {{ row.oldDurationMinutes }}→{{ row.newDurationMinutes }} 分
          </template>
        </el-table-column>
      </mes-table>
    </el-dialog>

    <!-- 代报工（docs/94，管理员/班组长） -->
    <el-dialog v-model="proxyDlg" title="代报工" width="720px" class="proxy-dialog"
      :fullscreen="proxyNarrow" :style="proxyNarrow ? { height: proxyViewportHeight + 'px' } : {}"
      :close-on-click-modal="false" :before-close="closeProxy" @closed="onProxyClosed">
      <p v-if="orderOptionsError" role="alert">工单选项加载失败 <el-button link @click="loadOrderOptions">重试</el-button></p>
      <el-form label-width="90px" :label-position="proxyNarrow ? 'top' : 'right'" :disabled="proxySubmitting || proxyLoading">
        <el-form-item label="工单号" required>
          <el-select
            v-model="proxyForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onProxyOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="proxyDetail" label="产品" class="proxy-product">
          <span>{{ proxyDetail.productName }}（计划 {{ proxyDetail.qty }}）</span>
        </el-form-item>
        <el-form-item label="工序" required>
          <el-select
            v-model="proxyForm.operationId"
            style="width:100%"
            placeholder="请先加载工单"
            :disabled="!proxyDetail"
            @change="onProxyOpChange"
          >
            <el-option
              v-for="o in (proxyDetail?.tasks || [])"
              :key="o.operationId"
              :label="`${o.seq} ${o.operationName}`"
              :value="o.operationId"
            />
          </el-select>
        </el-form-item>
        <p v-if="proxyLoadError" class="proxy-error" role="alert">{{ proxyLoadError }} <el-button link type="primary" :disabled="proxySubmitting || proxyLoading" @click="retryProxyLoad">重试</el-button></p>
        <p v-if="proxyLoading" role="status">正在加载工单及可代报人员…</p>
        <p v-else-if="proxyDetail && !proxyDetail.tasks?.length" role="status">该工单暂无可选工序</p>
        <ProcessingRequirements :price="proxyDetail?.tasks?.find(t=>t.operationId===proxyForm.operationId)?.processingPrice" />
        <section class="proxy-details">
          <h3>代报明细</h3>
          <p class="proxy-hint">记到所选工人名下，各人分别填写数量。</p>
          <p class="proxy-hint">仅可选该工序报权部门人员{{ isLeader ? '，且限本组' : '' }}。</p>
          <p v-if="proxyForm.operationId && !proxyLoading && !proxyLoadError && !proxyCandidates.length" role="status">当前工序暂无可代报人员</p>
          <article v-for="(row, idx) in proxyForm.rows" :key="row.localId" class="proxy-person">
            <header class="proxy-person-header">
              <b>工人{{ idx + 1 }}</b>
              <el-button link type="danger" :disabled="proxyForm.rows.length <= 1 || proxySubmitting || proxyLoading" @click="proxyForm.rows.splice(idx, 1)">移除</el-button>
            </header>
            <label :for="'proxy-user-' + row.localId">工人</label>
            <el-select :id="'proxy-user-' + row.localId" v-model="row.userId" filterable placeholder="选择工人" :disabled="!proxyForm.operationId || !!proxyLoadError">
              <el-option v-for="p in proxyCandidates" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
            <label :for="'proxy-good-' + row.localId">良品数量</label>
            <el-input-number :id="'proxy-good-' + row.localId" v-model="row.goodQty" :min="0" />
            <label :for="'proxy-defect-' + row.localId">不良数量</label>
            <el-input-number :id="'proxy-defect-' + row.localId" v-model="row.defectQty" :min="0" />
            <template v-if="row.defectQty > 0">
              <label :for="'proxy-reason-' + row.localId">不良原因</label>
              <el-select :id="'proxy-reason-' + row.localId" v-model="row.defectId" clearable placeholder="选择不良原因" :disabled="!proxyForm.operationId || !!proxyLoadError">
                <el-option v-for="d in proxyDefects" :key="d.id" :label="d.name" :value="d.id" />
              </el-select>
            </template>
            <template v-if="isAdmin">
              <template v-if="proxyNarrow">
                <label :for="'proxy-start-' + row.localId">计时补报开始时间</label>
                <input :id="'proxy-start-' + row.localId" type="datetime-local" step="1" :value="row.timeRange?.[0] || ''" :disabled="proxySubmitting || proxyLoading" @input="setProxyTime(row, 0, $event.target.value)" />
                <label :for="'proxy-end-' + row.localId">计时补报结束时间</label>
                <input :id="'proxy-end-' + row.localId" type="datetime-local" step="1" :value="row.timeRange?.[1] || ''" :disabled="proxySubmitting || proxyLoading" @input="setProxyTime(row, 1, $event.target.value)" />
              </template>
              <template v-else>
                <label>计时补报起止时间</label>
                <el-date-picker v-model="row.timeRange" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss" start-placeholder="计时补报开始" end-placeholder="结束" />
              </template>
            </template>
          </article>
          <el-button plain type="primary" :disabled="proxySubmitting || proxyLoading" @click="addProxyRow">+ 添加工人</el-button>
        </section>
      </el-form>
      <template #footer>
        <div class="proxy-footer">
          <div class="proxy-total">合计良品 {{ proxyGoodSum }} · 不良 {{ proxyDefectSum }}</div>
          <div class="proxy-actions">
            <el-button :disabled="proxySubmitting" @click="proxyDlg=false">取消</el-button>
            <el-button type="primary" :loading="proxySubmitting" :disabled="proxyLoading || !!proxyLoadError || !proxyDetail || !proxyForm.operationId" @click="onProxySubmit">确认代报</el-button>
          </div>
        </div>
      </template>
    </el-dialog>

    <!-- 一键补报未完工序（docs/17，仅管理员） -->
    <el-dialog v-model="batchDlg" title="一键补报未完工序" width="720px" class="report-management-dialog"
      :fullscreen="proxyNarrow" :style="proxyNarrow ? { height: proxyViewportHeight + 'px' } : {}"
      :close-on-click-modal="false" :before-close="closeBatch" @closed="onBatchClosed">
      <p v-if="orderOptionsError" role="alert">工单选项加载失败 <el-button link @click="loadOrderOptions">重试</el-button></p>
      <p v-if="batchLoadError" role="alert">{{ batchLoadError }} <el-button link @click="loadBatchOrder">重试</el-button></p>
      <el-form label-width="100px" :label-position="proxyNarrow ? 'top' : 'right'" :disabled="batchSubmitting || batchLoading">
        <el-form-item label="工单号" required>
          <el-select
            v-model="batchForm.orderNo"
            filterable
            clearable
            allow-create
            default-first-option
            placeholder="选择或输入工单号"
            style="width:100%"
            @change="onBatchOrderChange"
          >
            <el-option
              v-for="o in orderOptions"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="batchDetail" label="产品">
          <span>{{ batchDetail.productName }}（计划 {{ batchDetail.qty }}）</span>
        </el-form-item>
        <el-form-item v-if="batchDetail" label="待报工序">
          <div v-if="proxyNarrow" class="management-records">
            <article v-for="row in pendingTasks" :key="row.operationId" class="management-record">
              <b>{{ row.seq }} · {{ row.operationName }}</b>
              <div>计划 {{ row.planQty }} · 已报良品 {{ row.doneQty }}</div>
              <div>剩余可报 {{ row.remain }}</div>
            </article>
            <p v-if="!pendingTasks.length">所有工序已报满</p>
          </div>
          <mes-table table-key="views/report/index:3" v-else :data="pendingTasks" size="small" border empty-text="所有工序已报满">
            <el-table-column prop="seq" label="序号" width="60" />
            <el-table-column prop="operationName" label="工序" />
            <el-table-column prop="planQty" label="计划" width="80" />
            <el-table-column prop="doneQty" label="已报良品" width="90" />
            <el-table-column label="剩余可报" width="90">
              <template #default="{ row }">{{ row.remain }}</template>
            </el-table-column>
          </mes-table>
        </el-form-item>
        <el-form-item v-if="pendingTasks.length" label="统一良品数" required>
          <el-input-number v-model="batchForm.goodQty" :min="0" :max="minRemain" />
          <span class="hint">上限 = 各待报工序剩余最小值（{{ minRemain }}）</span>
        </el-form-item>
        <el-form-item v-if="pendingTasks.length" label="计时起止">
          <ManagementTimeRange v-model="batchTimes" :narrow="proxyNarrow" :disabled="batchSubmitting || batchLoading" start-label="计时开始时间" end-label="计时结束时间" />
          <span class="hint">计时岗必填；多人时间不同时在下方逐人填写</span>
        </el-form-item>
        <el-form-item v-if="pendingTasks.length" label="谁做的">
          <div class="alloc-wrap">
            <div class="hint">默认挂当前管理员；需要计件到人时再按工序拆分，每道合计必须等于统一良品数。</div>
            <el-checkbox v-model="batchForm.split">按工序拆分到人</el-checkbox>
            <div v-if="batchForm.split" class="alloc-list">
              <div v-for="row in batchForm.allocRows" :key="row.operationId" class="alloc-op">
                <div class="alloc-op-title">
                  {{ row.seq }} {{ row.operationName }}
                  <span :class="{ bad: rowSum(row) !== batchForm.goodQty }">
                    合计 {{ rowSum(row) }} / {{ batchForm.goodQty }}
                  </span>
                  <el-button link type="primary" @click="addAllocUser(row)">+ 加人</el-button>
                </div>
                <div v-for="(u, idx) in row.users" :key="u.localId" class="alloc-user management-alloc-user">
                  <span v-if="proxyNarrow" class="management-mobile-label">报工人</span>
                  <el-select v-model="u.userId" filterable placeholder="选人" style="width:180px">
                    <el-option v-for="p in people" :key="p.id" :label="`${p.name}（${p.account}）`" :value="p.id" />
                  </el-select>
                  <span v-if="proxyNarrow" class="management-mobile-label">良品数量</span>
                  <el-input-number v-model="u.goodQty" :min="0" />
                  <ManagementTimeRange v-model="u.timeRange" :narrow="proxyNarrow" :disabled="batchSubmitting || batchLoading" start-label="本人开始时间" end-label="本人结束时间" />
                  <el-button link type="danger" :disabled="row.users.length <= 1" @click="row.users.splice(idx, 1)">移除</el-button>
                </div>
              </div>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="batchSubmitting" @click="batchDlg=false">取消</el-button>
        <el-button
          type="primary"
          :loading="batchSubmitting"
          :disabled="!pendingTasks.length || batchLoading || !!batchLoadError"
          @click="onBatchSubmit"
        >确认补报</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="pendingDlg" title="待补报开工记录" width="920px" class="report-management-dialog"
      :fullscreen="proxyNarrow" :style="proxyNarrow ? { height: proxyViewportHeight + 'px' } : {}"
      :close-on-click-modal="false" :before-close="closePending" @closed="returnFromManagement('pending')">
      <p v-if="pendingError" role="alert">开工记录加载失败 <el-button link @click="openPending">重试</el-button></p>
      <p v-if="pendingLoading" role="status">正在加载开工记录…</p>
      <div v-else-if="proxyNarrow && !pendingError" class="management-records">
        <p v-if="!pendingRows.length">暂无待补报开工记录</p>
        <article v-for="row in pendingRows" :key="row.id" class="management-record">
          <b>{{ row.userName }} · {{ row.operationName }}</b>
          <div>工单 {{ row.orderNo }}</div><div>{{ row.productName }}</div>
          <div>开始 {{ row.startTime?.replace('T', ' ') }}</div><div>收尾 {{ row.endTime?.replace('T', ' ') }}</div>
          <el-button type="primary" plain @click="selectPending(row)">补报</el-button>
        </article>
      </div>
      <mes-table table-key="views/report/index:4" v-else-if="!pendingError && !pendingLoading" :data="pendingRows">
        <el-table-column prop="userName" label="人员" />
        <el-table-column prop="orderNo" label="工单" />
        <el-table-column prop="productName" label="产品" />
        <el-table-column prop="operationName" label="工序" />
        <el-table-column prop="startTime" label="开始" width="175" />
        <el-table-column prop="endTime" label="收尾" width="175" />
        <el-table-column label="时长"><template #default="{row}">{{ Math.ceil((new Date(row.endTime)-new Date(row.startTime))/60000) }}分</template></el-table-column>
        <el-table-column label="操作"><template #default="{row}"><el-button link @click="selectPending(row)">补报</el-button></template></el-table-column>
      </mes-table>
      <template #footer><el-button :disabled="pendingSaving" @click="pendingDlg=false">返回</el-button></template>
    </el-dialog>
    <el-dialog v-model="pendingEditDlg" title="补报开工记录" width="540px" class="report-management-dialog"
      :fullscreen="proxyNarrow" :style="proxyNarrow ? { height: proxyViewportHeight + 'px' } : {}"
      :close-on-click-modal="false" :before-close="closePending">
      <p>{{ pendingSelected?.userName }} · {{ pendingSelected?.orderNo }} · {{ pendingSelected?.operationName }}</p>
      <ManagementTimeRange v-model="pendingTimes" :narrow="proxyNarrow" :disabled="pendingSaving" />
      <p>良品数（计时岗可为0） <el-input-number v-model="pendingGood" :min="0" :disabled="pendingSaving" /></p>
      <template #footer><el-button :disabled="pendingSaving" @click="pendingEditDlg=false">取消</el-button><el-button type="primary" :loading="pendingSaving" @click="savePending">确认补报</el-button></template>
    </el-dialog>
  </div>
</template>
<script setup>
import ProcessingRequirements from '@/components/ProcessingRequirements.vue'
import { ref, computed, onMounted, onActivated, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getReportList, updateReport, submitReport, batchReport, getDefectsByOp, getOrderByNo, getOrderList, getReportCandidates, getReportChangeLogs } from '@/api/prod'
import { getUserList } from '@/api/sys'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getPendingWorkSessions, reportPendingWorkSession } from '@/api/workSession'
import { isUpstreamSoftConfirm } from '@/utils/upstreamConfirm'
import ValueTip from '@/components/ValueTip.vue'
import { VALUE_TIPS } from '@/constants/valueTips'
import ManagementTimeRange from '@/components/ManagementTimeRange.vue'
import { canOpenReportManagement, isReportManagementEntry, managementTimeError } from '@/utils/reportManagement'
import { USER_TYPE_OPTIONS, userTypeLabel, userTypeTagType } from '@/constants/userType'

const route = useRoute()
const router = useRouter()
const isAdmin = Number(localStorage.getItem('role') || 0) === 1
const isLeader = Number(localStorage.getItem('role') || 0) === 3
const canProxy = isAdmin || isLeader
const managementEntry = computed(() => isReportManagementEntry(route.query))
const managementAllowed = computed(() => canOpenReportManagement(route.query.action, localStorage.getItem('role')))
function returnFromManagement(action) {
  if (action === 'pending') { pendingLoadVersion++; pendingLoading.value = false }
  if (managementEntry.value && route.query.action === action) router.replace('/h5/home')
}
function onProxyClosed() { resetProxy(); returnFromManagement('proxy') }
function onBatchClosed() { resetBatch(); returnFromManagement('batch') }
function closeBatch(done) { if (!batchSubmitting.value) done() }
function closePending(done) { if (!pendingSaving.value) done() }
const orderOptionsError = ref(false)
const batchLoading = ref(false)
const batchLoadError = ref('')
const pendingError = ref(false)
let batchLoadVersion = 0
let pendingLoadVersion = 0
const list = ref([]), dlg = ref(false), createDlg = ref(false), defectOptions = ref([])
const logDlg = ref(false), changeLogs = ref([]), logLoading = ref(false)
const submitting = ref(false)
const orderDetail = ref(null)
const orderOptions = ref([])
const q = ref({ orderNo: '', productCode: '', productName: '', userType: null })
const form = ref({
  id: null, operationId: null, operationName: '',
  goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
})
const createForm = ref({
  orderNo: '', operationId: null, goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
})
const createClientRequestId = ref('')
const myUserId = Number(localStorage.getItem('userId') || 0)
const shareEnabled = ref(false)
const shareMode = ref(1)
const createCandidates = ref([])
const candidatesLoadError = ref(false)
const participantIds = ref([])
const participantDurations = ref({})

const proxyNarrow = ref(window.matchMedia('(max-width: 767px)').matches)
const proxyViewportHeight = ref(window.visualViewport?.height || window.innerHeight)
const proxyMedia = window.matchMedia('(max-width: 767px)')
function updateProxyViewport() {
  proxyNarrow.value = proxyMedia.matches
  proxyViewportHeight.value = window.visualViewport?.height || window.innerHeight
}
onMounted(() => {
  proxyMedia.addEventListener('change', updateProxyViewport)
  window.visualViewport?.addEventListener('resize', updateProxyViewport)
  window.addEventListener('resize', updateProxyViewport)
})
onUnmounted(() => {
  proxyMedia.removeEventListener('change', updateProxyViewport)
  window.visualViewport?.removeEventListener('resize', updateProxyViewport)
  window.removeEventListener('resize', updateProxyViewport)
  proxyOrderVersion++; proxyOpVersion++; batchLoadVersion++; pendingLoadVersion++
})
let proxyRowSequence = 0
let proxyOrderVersion = 0
let proxyOpVersion = 0
const proxyLoading = ref(false)
const proxyLoadError = ref('')
function newProxyRow() {
  return { localId: ++proxyRowSequence, userId: null, goodQty: 0, defectQty: 0, defectId: null, timeRange: null }
}
function setProxyTime(row, index, value) {
  const range = [...(row.timeRange || ['', ''])]
  range[index] = value && value.length === 16 ? value + ':00' : value
  row.timeRange = range.some(Boolean) ? range : null
}
const proxyDlg = ref(false)
const proxySubmitting = ref(false)
const proxyDetail = ref(null)
const proxyCandidates = ref([])
const proxyDefects = ref([])
const proxyClientRequestId = ref('')
const proxyForm = ref({
  orderNo: '', operationId: null, rows: [newProxyRow()]
})
const proxyGoodSum = computed(() =>
  (proxyForm.value.rows || []).reduce((s, r) => s + (Number(r.goodQty) || 0), 0)
)

const proxyDefectSum = computed(() => proxyForm.value.rows.reduce((sum, row) => sum + (Number(row.defectQty) || 0), 0))
watch(() => proxyForm.value.rows.map(row => row.defectQty), () => {
  for (const row of proxyForm.value.rows) if (!(row.defectQty > 0)) row.defectId = null
}, { flush: 'sync' })
function closeProxy(done) { if (!proxySubmitting.value) done() }

const batchDlg = ref(false)
const createTimes = ref(null)
const participantTimes = ref({})
const batchTimes = ref(null)
const pendingDlg = ref(false)
const pendingLoading = ref(false)
const pendingRows = ref([])
const pendingEditDlg = ref(false)
const pendingSelected = ref(null)
const pendingTimes = ref(null)
const pendingGood = ref(0)
const pendingSaving = ref(false)
const pendingRequestId = ref('')
function timeFields(range) { return range?.length === 2 ? { startTime: range[0], endTime: range[1] } : {} }
async function openPending() {
  if (!isAdmin) return
  const version = ++pendingLoadVersion
  pendingDlg.value = true
  pendingLoading.value = true
  pendingError.value = false
  try {
    const res = await getPendingWorkSessions()
    if (version === pendingLoadVersion && pendingDlg.value) pendingRows.value = res.data || []
  } catch (error) {
    if (version !== pendingLoadVersion) return
    pendingRows.value = []; pendingError.value = true
    console.error('[补报开工记录] 加载待处理记录失败', error)
  } finally { if (version === pendingLoadVersion) pendingLoading.value = false }
}
function selectPending(row) {
  if (!isAdmin || pendingSaving.value) return
  pendingSelected.value = row
  pendingTimes.value = [row.startTime, row.endTime]
  pendingGood.value = 0
  pendingRequestId.value = newClientRequestId()
  pendingEditDlg.value = true
}
async function savePending() {
  if (!isAdmin || pendingSaving.value) return
  if (!pendingTimes.value?.some(Boolean)) { ElMessage.error('请填写起止时间'); return }
  const timeError = managementTimeError(pendingTimes.value)
  if (timeError) { ElMessage.error(timeError); return }
  pendingSaving.value = true
  try {
    await reportPendingWorkSession(pendingSelected.value.id, { ...timeFields(pendingTimes.value), goodQty: pendingGood.value, clientRequestId: pendingRequestId.value })
    ElMessage.success('补报成功')
    pendingEditDlg.value = false
    if (managementEntry.value) pendingDlg.value = false
    else { await openPending(); await load() }
  } finally { pendingSaving.value = false }
}
const batchSubmitting = ref(false)
const batchDetail = ref(null)
const people = ref([])
const batchForm = ref({
  orderNo: '', goodQty: 1, split: false, allocRows: []
})

const summary = computed(() => {
  if (!orderDetail.value || !createForm.value.operationId) {
    return { plan: '—', done: '—', defect: '—', remain: '—' }
  }
  const t = (orderDetail.value.tasks || []).find(x => x.operationId === createForm.value.operationId)
  if (!t) return { plan: '—', done: '—', defect: '—', remain: '—' }
  return { plan: t.planQty, done: t.doneQty, defect: t.defectQty, remain: Math.max(0, t.planQty - t.doneQty) }
})

const remainNum = computed(() => {
  const r = summary.value.remain
  return typeof r === 'number' ? r : 0
})
const locked = computed(() => !!createForm.value.operationId && remainNum.value <= 0)

const sharePreview = computed(() => {
  if (!shareEnabled.value || !participantIds.value.length) return []
  const ids = [...new Set(participantIds.value.map(Number))]
  const n = ids.length
  if (n < 1) return []
  const good = Number(createForm.value.goodQty) || 0
  const defect = Number(createForm.value.defectQty) || 0
  const goods = new Array(n).fill(0)
  const defectsArr = new Array(n).fill(0)
  if (shareMode.value === 2) {
    const filled = ids.map(id => Number(participantDurations.value[id] || 0)).filter(d => d > 0)
    if (filled.length === 0) {
      const goodBase = Math.floor(good / n)
      const goodRem = good % n
      const defectBase = Math.floor(defect / n)
      const defectRem = defect % n
      ids.forEach((id, i) => {
        const isMe = id === myUserId
        goods[i] = goodBase + (isMe ? goodRem : 0)
        defectsArr[i] = defectBase + (isMe ? defectRem : 0)
      })
    } else {
      const avgFilled = Math.max(1, Math.floor(filled.reduce((s, d) => s + d, 0) / filled.length))
      const weights = ids.map(id => {
        const d = Number(participantDurations.value[id] || 0)
        return d > 0 ? d : avgFilled
      })
      const sumW = weights.reduce((s, w) => s + w, 0)
      let gAlloc = 0
      let dAlloc = 0
      let reporterIdx = 0
      ids.forEach((id, i) => {
        goods[i] = Math.floor((good * weights[i]) / sumW)
        defectsArr[i] = Math.floor((defect * weights[i]) / sumW)
        gAlloc += goods[i]
        dAlloc += defectsArr[i]
        if (id === myUserId) reporterIdx = i
      })
      goods[reporterIdx] += good - gAlloc
      defectsArr[reporterIdx] += defect - dAlloc
    }
  } else {
    const goodBase = Math.floor(good / n)
    const goodRem = good % n
    const defectBase = Math.floor(defect / n)
    const defectRem = defect % n
    ids.forEach((id, i) => {
      const isMe = id === myUserId
      goods[i] = goodBase + (isMe ? goodRem : 0)
      defectsArr[i] = defectBase + (isMe ? defectRem : 0)
    })
  }
  return ids.map((id, i) => {
    const c = createCandidates.value.find(x => Number(x.id) === id)
    return {
      userId: id,
      name: c?.name || (id === myUserId ? (localStorage.getItem('userName') || '我') : String(id)),
      good: goods[i],
      defect: defectsArr[i]
    }
  })
})

function shareGood(id) {
  const hit = sharePreview.value.find(p => p.userId === Number(id))
  return hit ? hit.good : 0
}

const pendingTasks = computed(() => {
  if (!batchDetail.value) return []
  return (batchDetail.value.tasks || [])
    .map(t => ({
      ...t,
      remain: Math.max(0, (t.planQty || 0) - (t.doneQty || 0))
    }))
    .filter(t => t.remain > 0)
})

const minRemain = computed(() => {
  if (!pendingTasks.value.length) return 0
  return Math.min(...pendingTasks.value.map(t => t.remain))
})

function rowSum(row) {
  return (row.users || []).reduce((s, u) => s + (u.goodQty || 0), 0)
}

function formatDuration(m) {
  const n = m || 0
  if (n <= 0) return '—'
  const h = Math.floor(n / 60)
  const min = n % 60
  if (h > 0 && min > 0) return `${h}小时${min}分钟`
  if (h > 0) return `${h}小时`
  return `${min}分钟`
}

onMounted(async () => {
  if (managementEntry.value) {
    if (!managementAllowed.value) return
    if (route.query.action === 'proxy') openProxy()
    else if (route.query.action === 'batch') openBatch()
    else if (route.query.action === 'pending') openPending()
    return
  }
  if (!route.query.order) await Promise.all([load(), loadOrderOptions()])
})

// PC 标签页按路径缓存；从工单再次进入时不会重新 mounted。
onActivated(async () => {
  if (managementEntry.value) return
  const order = route.query.order
  if (!order) return
  openCreate(String(order))
  // 消费本次入口参数，返回菜单时不重复弹出，之后仍可再次报同一工单。
  await router.replace({ path: '/report', query: {} })
  await load()
})

async function loadOrderOptions() {
  orderOptionsError.value = false
  try {
    const res = await getOrderList({ excludeFinished: true, page: 1, pageSize: 500 })
    orderOptions.value = (res.data?.list || []).map(o => ({ value: o.orderNo, label: `${o.orderNo}（${o.productName || ''}）` }))
  } catch (error) {
    orderOptions.value = []; orderOptionsError.value = true
    console.error('[报工] 加载可选工单失败', error)
  }
}

async function load() {
  const params = {
    orderNo: q.value.orderNo || undefined,
    productCode: q.value.productCode || undefined,
    productName: q.value.productName || undefined,
    page: 1,
    pageSize: 100
  }
  if (q.value.userType != null) params.userType = q.value.userType
  const res = await getReportList(params)
  list.value = res.data.list || []
}

function onCreateOrderChange(val) {
  if (!val) {
    orderDetail.value = null
    return
  }
  loadOrder()
}

function onProxyOrderChange() {
  loadProxyOrder()
}

function onBatchOrderChange() { loadBatchOrder() }

function openCreate(orderNo) {
  resetCreate()
  if (orderNo) createForm.value.orderNo = orderNo
  createDlg.value = true
  loadOrderOptions()
  if (orderNo) loadOrder()
}

function openProxy() {
  if (!canProxy) return
  resetProxy()
  proxyDlg.value = true
  loadOrderOptions()
}

function openBatch() {
  if (!isAdmin) return
  resetBatch()
  batchDlg.value = true
  loadOrderOptions()
}

function newClientRequestId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return 'r-' + Date.now() + '-' + Math.random().toString(36).slice(2, 10)
}

function resetCreate() {
  createTimes.value = null
  participantTimes.value = {}
  orderDetail.value = null
  defectOptions.value = []
  createClientRequestId.value = ''
  shareEnabled.value = false
  shareMode.value = 1
  createCandidates.value = []
  candidatesLoadError.value = false
  participantIds.value = []
  participantDurations.value = {}
  createForm.value = {
    orderNo: '', operationId: null, goodQty: 0, defectQty: 0, defectId: null, hours: 0, minutes: 0
  }
}

async function loadOrder() {
  const no = (createForm.value.orderNo || '').trim()
  if (!no) {
    ElMessage.error('请输入工单编号')
    return
  }
  const res = await getOrderByNo(no)
  const data = res.data
  if (data.status === 2 || data.status === 3) {
    ElMessage.error(data.status === 2 ? '工单已结束，不可报工' : '工单已取消，不可报工')
    orderDetail.value = null
    return
  }
  orderDetail.value = data
  createForm.value.operationId = null
  createForm.value.defectId = null
  defectOptions.value = []
  const tasks = data.tasks || []
  if (tasks.length === 1) {
    createForm.value.operationId = tasks[0].operationId
    await onOpChange()
  }
}

async function reloadCreateCandidates() {
  candidatesLoadError.value = false
  if (!createForm.value.operationId) {
    createCandidates.value = []
    return
  }
  try {
    const c = await getReportCandidates(Number(createForm.value.operationId))
    createCandidates.value = c.data || []
    if (myUserId && !participantIds.value.map(Number).includes(myUserId)) {
      participantIds.value = [myUserId]
    }
  } catch (error) {
    createCandidates.value = []
    candidatesLoadError.value = true
    console.error('[新增报工] 参与人列表加载失败', error)
    ElMessage.error('参与人列表加载失败')
  }
}

async function onOpChange() {
  createForm.value.defectId = null
  participantIds.value = myUserId ? [myUserId] : []
  participantDurations.value = {}
  createCandidates.value = []
  if (!createForm.value.operationId) {
    defectOptions.value = []
    return
  }
  const res = await getDefectsByOp(createForm.value.operationId)
  defectOptions.value = res.data || []
  await reloadCreateCandidates()
}

async function onCreate(keepOpen) {
  if (!orderDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!createForm.value.operationId) {
    ElMessage.error('请选择工序')
    return
  }
  if (locked.value) {
    ElMessage.error('该工序已报满，不可继续报工')
    return
  }
  if (createForm.value.defectQty > 0 && !createForm.value.defectId) {
    ElMessage.error('有不良品时必须选择不良品原因')
    return
  }
  const durationMinutes = (createForm.value.hours || 0) * 60 + (createForm.value.minutes || 0)
  if (durationMinutes > 1440) {
    ElMessage.error('单次报工时长不能超过24小时')
    return
  }
  const shareIds = [...new Set(participantIds.value.map(Number))]
  if (shareEnabled.value) {
    if (candidatesLoadError.value) {
      ElMessage.error('参与人列表加载失败，请重试后再提交')
      return
    }
    if (shareIds.length < 1) {
      ElMessage.error('请勾选参与人')
      return
    }
    if (myUserId && !shareIds.includes(myUserId)) {
      ElMessage.error('参与人须包含自己')
      return
    }
  }
  submitting.value = true
  try {
    if (!createClientRequestId.value) createClientRequestId.value = newClientRequestId()
    const payload = {
      orderId: orderDetail.value.id,
      operationId: createForm.value.operationId,
      goodQty: createForm.value.goodQty || 0,
      defectQty: createForm.value.defectQty || 0,
      defectId: createForm.value.defectId,
      durationMinutes: createTimes.value?.length ? null : (durationMinutes || null),
      ...timeFields(createTimes.value),
      clientRequestId: createClientRequestId.value
    }
    if (shareEnabled.value) {
      payload.shareMode = shareMode.value === 2 ? 2 : 1
      payload.participants = shareIds.map(id => ({ userId: id,
        durationMinutes: participantTimes.value[id]?.length ? null : (participantDurations.value[id] ?? null),
        ...timeFields(participantTimes.value[id]) }))
    }
    const res = await submitReportWithUpstreamConfirm(payload)
    createClientRequestId.value = ''
    const reports = res?.data?.reports || []
    const item = reports[0]
    const unmatched = reports.some(r => r.unitPrice == null)
    const goodSum = reports.reduce((s, r) => s + (Number(r.goodQty) || 0), 0)
    let tip = reports.length > 1
      ? `已为 ${reports.length} 人分摊，合计良品 ${goodSum}`
      : (item ? `报工成功：良品${item.goodQty} 不良${item.defectQty}` : '报工成功')
    if (unmatched) tip += '（未配工价）'
    if (unmatched) ElMessage.warning(tip)
    else ElMessage.success(tip)
    await load()
    if (keepOpen) {
      createTimes.value = null
      participantTimes.value = {}
      participantDurations.value = {}
      createForm.value.goodQty = 0
      createForm.value.defectQty = 0
      createForm.value.defectId = null
      createForm.value.hours = 0
      createForm.value.minutes = 0
      await loadOrder()
    } else {
      createDlg.value = false
    }
  } catch {
    // soft 取消或 http 已提示
  } finally {
    submitting.value = false
  }
}

/** docs/150：soft 确认后带 confirmUpstream 重提 */
async function submitReportWithUpstreamConfirm(payload) {
  try {
    return await submitReport({ ...payload, confirmUpstream: false })
  } catch (e) {
    if (!isUpstreamSoftConfirm(e)) throw e
    await ElMessageBox.confirm(e.message || '紧邻上一道复核通过量不足，是否继续报工？', '上级工序提示', {
      type: 'warning',
      confirmButtonText: '继续报工',
      cancelButtonText: '取消'
    })
    return await submitReport({ ...payload, confirmUpstream: true })
  }
}

async function batchReportWithUpstreamConfirm(payload) {
  try {
    return await batchReport({ ...payload, confirmUpstream: false })
  } catch (e) {
    if (!isUpstreamSoftConfirm(e)) throw e
    await ElMessageBox.confirm(e.message || '紧邻上一道复核通过量不足，是否继续报工？', '上级工序提示', {
      type: 'warning',
      confirmButtonText: '继续补报',
      cancelButtonText: '取消'
    })
    return await batchReport({ ...payload, confirmUpstream: true })
  }
}

function resetProxy() {
  proxyOrderVersion++; proxyOpVersion++
  proxyLoading.value = false
  proxyLoadError.value = ''
  proxyDetail.value = null
  proxyCandidates.value = []
  proxyDefects.value = []
  proxyClientRequestId.value = ''
  proxyForm.value = { orderNo: '', operationId: null, rows: [newProxyRow()] }
}

function addProxyRow() { proxyForm.value.rows.push(newProxyRow()) }

function clearProxySelections() {
  const hadSelection = proxyForm.value.rows.some(row => row.userId || row.defectId)
  for (const row of proxyForm.value.rows) { row.userId = null; row.defectId = null }
  if (hadSelection) ElMessage.warning('工单或工序已变化，请重新选择工人和不良原因；数量已保留，请核对')
}

function retryProxyLoad() {
  if (proxyDetail.value && proxyForm.value.operationId) onProxyOpChange()
  else loadProxyOrder()
}

async function loadProxyOrder() {
  const version = ++proxyOrderVersion
  ++proxyOpVersion
  const no = (proxyForm.value.orderNo || '').trim()
  proxyDetail.value = null
  proxyForm.value.operationId = null
  proxyCandidates.value = []; proxyDefects.value = []
  proxyLoadError.value = ''
  proxyLoading.value = !!no
  clearProxySelections()
  if (!no) return
  try {
    const { data } = await getOrderByNo(no)
    if (version !== proxyOrderVersion || !proxyDlg.value) return
    if (data.status === 2 || data.status === 3) {
      proxyLoadError.value = data.status === 2 ? '工单已结束，不可报工' : '工单已取消，不可报工'
      return
    }
    proxyDetail.value = data
    if (data.tasks?.length === 1) {
      proxyForm.value.operationId = data.tasks[0].operationId
      await onProxyOpChange()
    }
  } catch (error) {
    if (version !== proxyOrderVersion || !proxyDlg.value) return
    proxyLoadError.value = '加载代报工工单失败，请重试'
    console.error('[代报工] 加载工单失败', no, error)
  } finally {
    if (version === proxyOrderVersion) {
      proxyLoading.value = false
    }
  }
}

async function onProxyOpChange() {
  const version = ++proxyOpVersion
  const orderVersion = proxyOrderVersion
  const operationId = proxyForm.value.operationId
  proxyCandidates.value = []; proxyDefects.value = []
  proxyLoadError.value = ''
  if (!operationId) {
    proxyLoading.value = false
    clearProxySelections()
    return
  }
  proxyLoading.value = true
  try {
    const [c, d] = await Promise.all([getReportCandidates(operationId), getDefectsByOp(operationId)])
    if (version !== proxyOpVersion || orderVersion !== proxyOrderVersion || !proxyDlg.value) return
    proxyCandidates.value = c.data || []
    proxyDefects.value = d.data || []
    let cleared = false
    for (const row of proxyForm.value.rows) {
      if (row.userId && !proxyCandidates.value.some(p => p.id === row.userId)) { row.userId = null; cleared = true }
      if (row.defectId && !proxyDefects.value.some(d => d.id === row.defectId)) { row.defectId = null; cleared = true }
    }
    if (cleared) ElMessage.warning('已清除当前工序不适用的工人或不良原因；数量已保留，请核对')
  } catch (error) {
    if (version !== proxyOpVersion || orderVersion !== proxyOrderVersion || !proxyDlg.value) return
    proxyLoadError.value = '加载代报工人员或不良原因失败，请重试'
    console.error('[代报工] 加载工序人员和不良原因失败', operationId, error)
  } finally {
    if (version === proxyOpVersion && orderVersion === proxyOrderVersion) proxyLoading.value = false
  }
}

async function onProxySubmit() {
  if (proxySubmitting.value || proxyLoading.value || proxyLoadError.value) return
  if (!proxyDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!proxyForm.value.operationId) {
    ElMessage.error('请选择工序')
    return
  }
  const rows = proxyForm.value.rows || []
  if (!rows.length) {
    ElMessage.error('请至少添加一名被代报人')
    return
  }
  for (const [index, r] of rows.entries()) {
    const person = `工人${index + 1}`
    if (!r.userId) {
      ElMessage.error(`${person}：请选择被代报人`)
      return
    }
    if ((r.defectQty || 0) > 0 && !r.defectId) {
      ElMessage.error(`${person}：有不良品时必须选择不良品原因`)
      return
    }
    if (!proxyCandidates.value.some(p => p.id === r.userId)
      || (r.defectQty > 0 && !proxyDefects.value.some(d => d.id === r.defectId))) {
      ElMessage.error(`${person}：工人或不良原因不适用于当前工序，请重新选择`)
      return
    }
    if (r.timeRange?.some(Boolean)) {
      const [start, end] = r.timeRange
      if (!start || !end || !Number.isFinite(Date.parse(start)) || !Number.isFinite(Date.parse(end)) || Date.parse(end) <= Date.parse(start)) {
        ElMessage.error(`${person}：请补全开始和结束时间，结束须晚于开始`)
        return
      }
      if (Date.parse(end) - Date.parse(start) > 24 * 60 * 60 * 1000) {
        ElMessage.error(`${person}：单次计时补报不能超过24小时`)
        return
      }
    }
  }
  proxySubmitting.value = true
  try {
    if (!proxyClientRequestId.value) proxyClientRequestId.value = newClientRequestId()
    const res = await submitReportWithUpstreamConfirm({
      orderId: proxyDetail.value.id,
      operationId: proxyForm.value.operationId,
      goodQty: 0,
      defectQty: 0,
      clientRequestId: proxyClientRequestId.value,
      assignees: rows.map(r => ({
        userId: r.userId,
        goodQty: r.goodQty || 0,
        defectQty: r.defectQty || 0,
        defectId: r.defectQty > 0 ? r.defectId : null,
        durationMinutes: null,
        ...timeFields(r.timeRange)
      }))
    })
    proxyClientRequestId.value = ''
    const n = res?.data?.reports?.length || rows.length
    const unmatched = (res?.data?.reports || []).some(r => r.unitPrice == null)
    const tip = unmatched ? `已为 ${n} 人代报（含未配工价）` : `已为 ${n} 人代报`
    if (unmatched) ElMessage.warning(tip)
    else ElMessage.success(tip)
    proxyDlg.value = false
    if (!managementEntry.value) await load()
  } catch (error) {
    // 用户取消上游确认不记为接口故障，其他错误由http提示并保留输入和请求标识。
    if (error !== 'cancel' && error !== 'close') console.error('[代报工] 提交失败，保留输入供核对重试', error)
  } finally {
    proxySubmitting.value = false
  }
}

async function openEdit(row) {
  const defects = await getDefectsByOp(row.operationId)
  defectOptions.value = defects.data || []
  const dm = row.durationMinutes || 0
  form.value = {
    id: row.id,
    operationId: row.operationId,
    operationName: row.operationName,
    goodQty: row.goodQty,
    defectQty: row.defectQty,
    defectId: row.defectId,
    hours: Math.floor(dm / 60),
    minutes: dm % 60
  }
  dlg.value = true
}

async function openChangeLogs(row) {
  logDlg.value = true
  logLoading.value = true
  changeLogs.value = []
  try {
    const res = await getReportChangeLogs(row.id)
    changeLogs.value = res.data || []
  } finally {
    logLoading.value = false
  }
}

async function onSave() {
  if (form.value.defectQty > 0 && !form.value.defectId) {
    ElMessage.error('有不良品时必须选择不良品原因')
    return
  }
  const durationMinutes = (form.value.hours || 0) * 60 + (form.value.minutes || 0)
  if (durationMinutes > 1440) {
    ElMessage.error('单次报工时长不能超过24小时')
    return
  }
  await updateReport(form.value.id, {
    orderId: 0,
    operationId: form.value.operationId,
    goodQty: form.value.goodQty,
    defectQty: form.value.defectQty,
    defectId: form.value.defectId,
    durationMinutes,
    source: 1
  })
  ElMessage.success('已保存')
  dlg.value = false
  await load()
}

function resetBatch() {
  batchLoadVersion++; batchLoading.value = false; batchLoadError.value = ''
  batchTimes.value = null
  batchDetail.value = null
  batchForm.value = { orderNo: '', goodQty: 1, split: false, allocRows: [] }
}

async function ensurePeople() {
  if (people.value.length) return
  const res = await getUserList({ page: 1, pageSize: 200 })
  people.value = res.data?.list || []
}

async function loadBatchOrder() {
  const version = ++batchLoadVersion
  const no = (batchForm.value.orderNo || '').trim()
  batchDetail.value = null
  batchForm.value.allocRows = []
  batchLoadError.value = ''
  batchLoading.value = !!no
  if (!no) return
  try {
    const { data } = await getOrderByNo(no)
    if (version !== batchLoadVersion || !batchDlg.value) return
    if (data.status === 2 || data.status === 3) {
      batchLoadError.value = data.status === 2 ? '工单已结束，不可补报' : '工单已取消，不可补报'
      return
    }
    batchDetail.value = data
    const min = Math.min(...(data.tasks || []).map(t => Math.max(0, (t.planQty || 0) - (t.doneQty || 0))).filter(r => r > 0))
    batchForm.value.goodQty = Number.isFinite(min) && min > 0 ? min : 1
    rebuildAllocRows()
  } catch (error) {
    if (version !== batchLoadVersion) return
    batchLoadError.value = '补报工单加载失败，请重试'
    console.error('[一键补报] 加载工单失败', no, error)
  } finally { if (version === batchLoadVersion) batchLoading.value = false }
}

function rebuildAllocRows() {
  batchForm.value.allocRows = pendingTasks.value.map(t => ({
    operationId: t.operationId,
    seq: t.seq,
    operationName: t.operationName,
    users: [{ ...newProxyRow(), goodQty: batchForm.value.goodQty || 1 }]
  }))
}

function addAllocUser(row) {
  row.users.push({ ...newProxyRow(), goodQty: 1 })
}

watch(() => batchForm.value.split, async (v) => {
  if (v) {
    await ensurePeople()
    rebuildAllocRows()
  }
})

watch(() => batchForm.value.goodQty, (n) => {
  if (!batchForm.value.split) return
  for (const row of batchForm.value.allocRows) {
    if (row.users.length === 1) row.users[0].goodQty = n || 1
  }
})

async function onBatchSubmit() {
  if (!isAdmin || batchSubmitting.value || batchLoading.value || batchLoadError.value) return
  const timeError = managementTimeError(batchTimes.value)
  if (timeError) { ElMessage.error(timeError); return }
  for (const row of batchForm.value.split ? batchForm.value.allocRows : []) {
    for (const user of row.users) {
      const error = managementTimeError(user.timeRange)
      if (error) { ElMessage.error(`工序【${row.operationName}】：${error}`); return }
    }
  }
  if (!batchDetail.value) {
    ElMessage.error('请先加载工单')
    return
  }
  if (!pendingTasks.value.length) {
    ElMessage.error('所有工序已报满')
    return
  }
  const n = batchForm.value.goodQty || 0
  if (n < 0) {
    ElMessage.error('补报良品数不能为负')
    return
  }
  if (n > minRemain.value) {
    ElMessage.error(`本次数量超过剩余可报最小值 ${minRemain.value}`)
    return
  }

  let allocations = null
  if (batchForm.value.split) {
    for (const row of batchForm.value.allocRows) {
      if (rowSum(row) !== n) {
        ElMessage.error(`工序【${row.operationName}】拆分合计必须等于 ${n}`)
        return
      }
      for (const u of row.users) {
        if (!u.userId) {
          ElMessage.error(`工序【${row.operationName}】请选择报工人`)
          return
        }
      }
    }
    allocations = batchForm.value.allocRows.map(row => ({
      operationId: row.operationId,
      users: row.users.map(u => ({ userId: u.userId, goodQty: u.goodQty, ...timeFields(u.timeRange) }))
    }))
  }

  // batch_no 最长 32：UUID 去横线正好 32（docs/17）
  const batchNo = (crypto.randomUUID && crypto.randomUUID().replace(/-/g, ''))
    || `${Date.now()}${Math.random().toString(16).slice(2, 10)}`.slice(0, 32)

  batchSubmitting.value = true
  try {
    const res = await batchReportWithUpstreamConfirm({
      orderId: batchDetail.value.id,
      goodQty: n,
      batchNo,
      allocations,
      ...timeFields(batchTimes.value)
    })
    const data = res.data || {}
    ElMessage.success(`已为 ${data.operationCount || 0} 道工序生成 ${data.reportCount || 0} 条报工`)
    batchDlg.value = false
    if (!managementEntry.value) await load()
  } catch {
    // soft 取消或 http 已提示
  } finally {
    batchSubmitting.value = false
  }
}
</script>
<style scoped>
.toolbar{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap}
.duration{display:flex;gap:8px;align-items:center}
.summary{display:flex;gap:16px;color:#606266;font-size:13px}
.hint{margin-left:8px;color:#909399;font-size:12px}
.wage-miss{color:#f56c6c;margin-right:4px}
.alloc-wrap{width:100%}
.alloc-list{margin-top:8px;display:flex;flex-direction:column;gap:12px}
.alloc-op{border:1px solid #ebeef5;border-radius:4px;padding:8px}
.alloc-op-title{display:flex;gap:12px;align-items:center;margin-bottom:6px;font-weight:500}
.alloc-op-title .bad{color:#f56c6c}
.share-box { width: 100%; }
.share-person { display: flex; align-items: center; gap: 8px; margin: 4px 0; flex-wrap: wrap; }
.share-min { width: 120px; }
.share-unit { flex: none; font-size: 14px; line-height: 32px; color: #303133; }
.share-preview { margin-top: 8px; padding-top: 8px; border-top: 1px dashed #ebeef5; color: #606266; font-size: 13px; }
</style>

<style>
/* 仅作用于代报工弹窗，避免影响共用分摊/补报控件。 */
.el-dialog.proxy-dialog { display: flex; flex-direction: column; max-height: calc(100dvh - 64px); margin-top: 32px; }
.proxy-dialog .el-dialog__header, .proxy-dialog .el-dialog__footer { flex: none; }
.proxy-dialog .el-dialog__body { min-height: 0; overflow-y: auto; overscroll-behavior: contain; }
.proxy-dialog .proxy-product { overflow-wrap: anywhere; }
.proxy-dialog .proxy-details h3 { margin: 8px 0; font-size: 15px; }
.proxy-dialog .proxy-hint { margin: 4px 0; color: #606266; line-height: 1.5; font-size: 13px; }
.proxy-dialog .proxy-error { color: #c45656; }
.proxy-dialog .proxy-person { display: grid; grid-template-columns: 130px minmax(0, 1fr); align-items: center; gap: 10px; border: 1px solid #dcdfe6; border-radius: 8px; padding: 14px; margin: 12px 0; min-width: 0; }
.proxy-dialog .proxy-person-header { grid-column: 1 / -1; display: flex; justify-content: space-between; align-items: center; }
.proxy-dialog .proxy-person label { font-size: 14px; line-height: 1.4; color: #303133; }
.proxy-dialog .proxy-person .el-select, .proxy-dialog .proxy-person .el-input-number, .proxy-dialog .proxy-person .el-date-editor { width: 100%; min-width: 0; box-sizing: border-box; }
.proxy-dialog .proxy-footer { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
.proxy-dialog .proxy-total { color: #303133; font-size: 14px; overflow-wrap: anywhere; }
.proxy-dialog .proxy-actions { display: flex; gap: 10px; }
.proxy-dialog .proxy-actions .el-button + .el-button { margin-left: 0; }
@media (max-width: 767px) {
  .el-dialog.proxy-dialog.is-fullscreen { width: 100% !important; max-width: none; max-height: 100dvh; margin: 0 !important; border-radius: 0; padding: 12px 16px; box-sizing: border-box; }
  .proxy-dialog .el-dialog__body { padding: 8px 0; }
  .proxy-dialog .el-dialog__header { padding-bottom: 8px; }
  .proxy-dialog .el-dialog__footer { padding: 12px 0 max(4px, env(safe-area-inset-bottom)); border-top: 1px solid #ebeef5; }
  .proxy-dialog .el-form-item__label { padding: 0 0 6px; line-height: 22px; }
  .proxy-dialog .proxy-person { grid-template-columns: minmax(0, 1fr); gap: 7px; padding: 12px; }
  .proxy-dialog .proxy-person label { margin-top: 5px; }
  .proxy-dialog .proxy-person .el-input-number, .proxy-dialog .proxy-person .el-select__wrapper { min-height: 42px; }
  .proxy-dialog .proxy-person input[type="datetime-local"] { display: block; width: 100%; min-width: 0; max-width: 100%; height: 42px; box-sizing: border-box; padding: 6px 8px; border: 1px solid #dcdfe6; border-radius: 4px; background: #fff; color: #303133; font: inherit; }
  .proxy-dialog .proxy-footer { display: block; }
  .proxy-dialog .proxy-total { margin-bottom: 10px; text-align: left; }
  .proxy-dialog .proxy-actions .el-button { flex: 1; min-height: 42px; }
  .proxy-dialog .el-input__inner { font-size: 16px; }
}
</style>

<style>
/* 275：只作用于补报弹窗，普通报工和代报沿用原布局。 */
.el-dialog.report-management-dialog { display: flex; flex-direction: column; max-height: calc(100dvh - 64px); margin-top: 32px; }
.report-management-dialog .el-dialog__header, .report-management-dialog .el-dialog__footer { flex: none; }
.report-management-dialog .el-dialog__body { min-height: 0; overflow-y: auto; overscroll-behavior: contain; }
.report-management-dialog .el-form-item__content { min-width: 0; }
.report-management-dialog p { overflow-wrap: anywhere; }
.management-entry { padding: 12px; }
.management-entry a { color: var(--app-primary); text-decoration: none; }
.management-records { width: 100%; min-width: 0; }
.management-record { border: 1px solid #dcdfe6; border-radius: 8px; padding: 12px; margin-bottom: 10px; line-height: 1.6; overflow-wrap: anywhere; }
.management-record .el-button { margin-top: 8px; }
@media (max-width: 767px) {
  .el-dialog.report-management-dialog.is-fullscreen { width: 100% !important; max-width: none; max-height: 100dvh; margin: 0 !important; border-radius: 0; padding: 12px 16px; box-sizing: border-box; }
  .report-management-dialog .el-dialog__body { padding: 8px 0; }
  .report-management-dialog .el-dialog__footer { padding: 12px 0 max(4px, env(safe-area-inset-bottom)); border-top: 1px solid #ebeef5; }
  .report-management-dialog .el-form-item__label { padding: 0 0 6px; line-height: 22px; }
  .report-management-dialog .el-form-item__content { display: block; }
  .report-management-dialog .hint { display: block; margin: 6px 0 0; line-height: 1.5; }
  .report-management-dialog .management-alloc-user { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px; margin: 10px 0; border: 1px solid #ebeef5; padding: 10px; border-radius: 6px; }
  .report-management-dialog .management-alloc-user .el-select, .report-management-dialog .management-alloc-user .el-input-number { width: 100% !important; min-width: 0; }
  .report-management-dialog .alloc-op-title { flex-wrap: wrap; line-height: 1.5; overflow-wrap: anywhere; }
  .report-management-dialog .el-input__inner { font-size: 16px; }
  .report-management-dialog .el-button { min-height: 40px; }
}
</style>
