<script setup lang="ts">
import { ref } from 'vue'
import type { RouteStep, WorkStep, WorkStepParam } from '../types/mes'

const props = defineProps<{
  steps: RouteStep[]
  loading: boolean
}>()

const expandedParams = ref<Record<string, boolean>>({})

function toggleParams(key: string) {
  expandedParams.value[key] = !expandedParams.value[key]
}

interface FlatWorkStep {
  seqIdx: number
  seqNo: string
  seqName: string
  wsIdx: number
  ws: WorkStep
  paramKey: string
  hasParam: boolean
  hasMaterial: boolean
  hasDetail: boolean
}

function flatten(): FlatWorkStep[] {
  const result: FlatWorkStep[] = []
  props.steps.forEach((seq, si) => {
    const list = (seq.workStepList as WorkStep[] | undefined) ?? []
    if (!list.length) {
      result.push({
        seqIdx: si,
        seqNo: seq.workseqNo ?? '-',
        seqName: seq.workseqName ?? '-',
        wsIdx: 0,
        ws: seq as unknown as WorkStep,
        paramKey: `${si}-0`,
        hasParam: false,
        hasMaterial: false,
        hasDetail: false
      })
      return
    }

    list.forEach((ws, wi) => {
      const hasParam = !!ws.workStepParamList?.length
      const hasMaterial = !!ws.workStepMaterialList?.length
      result.push({
        seqIdx: si,
        seqNo: seq.workseqNo ?? '-',
        seqName: seq.workseqName ?? '-',
        wsIdx: wi,
        ws,
        paramKey: `${si}-${wi}`,
        hasParam,
        hasMaterial,
        hasDetail: hasParam || hasMaterial
      })
    })
  })
  return result
}

function fmt(val: unknown): string {
  if (val === null || val === undefined || val === '') return '—'
  return String(val)
}

</script>

<template>
  <div class="route-table">
    <div class="table-header">
      <span>工步工序列表</span>
      <span v-if="loading" class="loading-badge">加载中...</span>
      <span v-else-if="steps.length" class="count-badge">
        {{ steps.length }} 工步 /
        {{ steps.reduce((s, x) => s + ((x.workStepList as WorkStep[] | undefined)?.length ?? 0), 0) }} 子步骤
      </span>
    </div>

    <div v-if="loading" class="skeleton-wrap">
      <div v-for="i in 4" :key="i" class="skeleton-row" />
    </div>

    <div v-else-if="!steps.length" class="empty-state">暂无工步数据，请先完成扫码查询</div>

    <div v-else class="table-scroll">
      <table>
        <thead>
          <tr>
            <th style="width: 36px">#</th>
            <th style="width: 44px">序</th>
            <th>工步编码</th>
            <th>子步骤名称</th>
            <th style="width: 52px">详情</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="row in flatten()" :key="row.paramKey">
            <tr
              class="data-row"
              :class="{ 'expanded-row': expandedParams[row.paramKey] }"
              :style="{ cursor: row.hasDetail ? 'pointer' : 'default' }"
              @click="row.hasDetail && toggleParams(row.paramKey)"
            >
              <td>
                <span v-if="row.wsIdx === 0" class="seq-badge">{{ row.seqIdx + 1 }}</span>
              </td>
              <td><span class="ws-badge">{{ row.wsIdx + 1 }}</span></td>
              <td class="mono">{{ row.ws.workstepNo ?? row.seqNo }}</td>
              <td class="step-name">{{ row.ws.workstepName ?? row.seqName }}</td>
              <td class="center">
                <span v-if="row.hasDetail" class="param-toggle">
                  {{ expandedParams[row.paramKey] ? '▼' : '▶' }}
                  <em v-if="row.hasParam">P{{ (row.ws.workStepParamList as WorkStepParam[]).length }}</em>
                  <em v-if="row.hasMaterial" class="mat-em">M{{ (row.ws.workStepMaterialList as any[]).length }}</em>
                </span>
                <span v-else class="muted">—</span>
              </td>
            </tr>

            <tr v-if="row.hasDetail && expandedParams[row.paramKey]" :key="row.paramKey + '-details'" class="param-row">
              <td colspan="5" class="param-cell">
                <div v-if="row.hasMaterial" class="param-table-wrap mb-2">
                  <div class="sub-table-title">物料绑定信息</div>
                  <table class="param-table">
                    <thead>
                      <tr>
                        <th style="width: 28px">序</th>
                        <th>物料编号</th>
                        <th>物料名称</th>
                        <th>数量</th>
                        <th>条码长度</th>
                        <th>追溯类型</th>
                        <th>校验长度</th>
                        <th>主键 ID</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(mat, mi) in (row.ws.workStepMaterialList as any[])" :key="'mat-' + mi" class="param-data-row obj-row">
                        <td><span class="param-idx obj-idx">{{ mi + 1 }}</span></td>
                        <td class="mono val-cell">{{ mat.material_No ?? '—' }}</td>
                        <td class="param-name">{{ mat.material_Name ?? '—' }}</td>
                        <td class="center">{{ mat.material_number ?? '—' }}</td>
                        <td class="center">{{ fmt(mat.noLength) }}</td>
                        <td class="center">{{ fmt(mat.retrospect_Type) }}</td>
                        <td class="center">{{ fmt(mat.isCheckLength) }}</td>
                        <td class="muted mono small">{{ mat.material_id ?? '—' }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>

                <div v-if="row.hasParam" class="param-table-wrap">
                  <div class="sub-table-title">工步参数</div>
                  <table class="param-table">
                    <thead>
                      <tr>
                        <th style="width: 28px">序</th>
                        <th>参数名称</th>
                        <th>最小值</th>
                        <th>最大值</th>
                        <th>标准值</th>
                        <th>单位</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="(param, pi) in (row.ws.workStepParamList as WorkStepParam[])" :key="pi" class="param-data-row">
                        <td><span class="param-idx">{{ pi + 1 }}</span></td>
                        <td class="param-name">{{ param.paramName ?? '—' }}</td>
                        <td class="mono val-cell">{{ fmt(param.minQualityValue) }}</td>
                        <td class="mono val-cell">{{ fmt(param.maxQualityValue) }}</td>
                        <td class="mono val-cell std">{{ fmt(param.standardValue) }}</td>
                        <td class="muted">{{ param.paramUnit ?? '—' }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.route-table {
  background: #131929;
  border: 1px solid rgba(100, 181, 246, 0.15);
  border-radius: 10px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  height: 100%;
}

.table-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  background: linear-gradient(90deg, rgba(13, 71, 161, 0.4), transparent);
  border-bottom: 1px solid rgba(100, 181, 246, 0.12);
  font-size: 13px;
  font-weight: 600;
  color: #90caf9;
  flex-shrink: 0;
}

.loading-badge {
  margin-left: auto;
  font-size: 11px;
  color: #42a5f5;
  animation: pulse 1.2s infinite;
}

.count-badge {
  margin-left: auto;
  background: rgba(66, 165, 245, 0.12);
  color: #42a5f5;
  padding: 2px 12px;
  border-radius: 20px;
  font-size: 11px;
}

.skeleton-wrap {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.skeleton-row {
  height: 34px;
  background: linear-gradient(90deg, #1a2035 25%, #202a42 50%, #1a2035 75%);
  background-size: 200% 100%;
  border-radius: 4px;
  animation: shimmer 1.5s infinite;
}

.empty-state {
  padding: 40px;
  text-align: center;
  color: #37474f;
  font-size: 13px;
}

.table-scroll {
  flex: 1;
  overflow: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}

thead tr {
  background: rgba(21, 101, 192, 0.2);
  position: sticky;
  top: 0;
  z-index: 2;
}

th {
  padding: 8px 12px;
  text-align: left;
  color: #78909c;
  font-weight: 600;
  white-space: nowrap;
  border-bottom: 1px solid rgba(100, 181, 246, 0.1);
}

.data-row {
  border-bottom: 1px solid rgba(100, 181, 246, 0.06);
  transition: background 0.15s;
}

.data-row:hover {
  background: rgba(66, 165, 245, 0.05);
}

.data-row.expanded-row {
  background: rgba(21, 101, 192, 0.1);
  border-bottom-color: transparent;
}

td {
  padding: 7px 12px;
  color: #cfd8dc;
  vertical-align: middle;
}

.center {
  text-align: center;
}

.mono {
  font-family: Consolas, monospace;
  color: #64b5f6;
}

.seq-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  background: linear-gradient(135deg, #1565c0, #0d47a1);
  border-radius: 50%;
  font-size: 11px;
  color: #e3f2fd;
  font-weight: 700;
}

.ws-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  background: rgba(100, 181, 246, 0.1);
  border: 1px solid rgba(100, 181, 246, 0.2);
  border-radius: 4px;
  font-size: 10px;
  color: #90caf9;
  font-weight: 600;
}

.step-name {
  color: #e0e6ed;
  font-weight: 500;
}

.param-toggle {
  cursor: pointer;
  color: #42a5f5;
  font-size: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 3px;
}

.param-toggle em {
  font-style: normal;
  background: rgba(66, 165, 245, 0.2);
  border-radius: 10px;
  padding: 0 5px;
  font-size: 10px;
}

.muted {
  color: #37474f;
}

.param-row {
  background: rgba(13, 71, 161, 0.06);
}

.param-cell {
  padding: 0 12px 10px 48px !important;
}

.param-table-wrap {
  border: 1px solid rgba(100, 181, 246, 0.12);
  border-radius: 6px;
  overflow: hidden;
}

.param-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 11px;
}

.param-table thead tr {
  background: rgba(21, 101, 192, 0.15);
}

.param-table th {
  padding: 6px 10px;
  color: #546e7a;
  font-size: 10px;
  border-bottom: 1px solid rgba(100, 181, 246, 0.08);
}

.param-data-row {
  border-bottom: 1px solid rgba(100, 181, 246, 0.05);
}

.param-data-row:last-child {
  border-bottom: none;
}

.param-data-row:hover {
  background: rgba(66, 165, 245, 0.04);
}

.param-table td {
  padding: 5px 10px;
  color: #b0bec5;
  vertical-align: middle;
}

.param-idx {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  background: rgba(100, 181, 246, 0.08);
  border-radius: 3px;
  font-size: 9px;
  color: #64b5f6;
}

.param-name {
  color: #e0e6ed;
  font-weight: 500;
}

.val-cell {
  color: #80cbc4;
  font-size: 11px;
}

.std {
  color: #a5d6a7 !important;
}

.mb-2 {
  margin-bottom: 8px;
}

.sub-table-title {
  padding: 6px 10px;
  font-size: 11px;
  font-weight: 600;
  color: #90caf9;
  background: rgba(13, 71, 161, 0.1);
  border-bottom: 1px solid rgba(100, 181, 246, 0.08);
}

.mat-em {
  background: rgba(171, 71, 188, 0.2) !important;
  color: #ce93d8 !important;
}

.obj-row:hover {
  background: rgba(171, 71, 188, 0.05);
}

.obj-idx {
  background: rgba(171, 71, 188, 0.1);
  color: #ce93d8;
}

.small {
  font-size: 10px;
}

@keyframes shimmer {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}
</style>
