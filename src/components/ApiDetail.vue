<script setup lang="ts">
import { ref } from 'vue'

export interface ApiRecord {
  title: string
  url: string
  reqBody: object
  resBody?: unknown
  status: 'pending' | 'success' | 'error'
  duration?: number
  time: string
}

defineProps<{
  records: ApiRecord[]
}>()

// 展开/折叠状态
const expanded = ref<Record<number, boolean>>({ 0: true, 1: true })

function toggle(idx: number) {
  expanded.value[idx] = !expanded.value[idx]
}

function formatJson(val: unknown): string {
  try {
    return JSON.stringify(val, null, 2)
  } catch {
    return String(val)
  }
}
</script>

<template>
  <div class="api-detail">
    <div class="panel-header">
      <span class="icon">🔌</span>
      <span>接口交互详情</span>
    </div>

    <div v-if="!records.length" class="empty-state">
      暂无交互记录，请扫码查询
    </div>

    <div v-else class="records-wrap">
      <div
        v-for="(rec, idx) in records"
        :key="idx"
        class="record-card"
        :class="rec.status"
      >
        <!-- 卡片标题行 -->
        <div class="record-header" @click="toggle(idx)">
          <span class="status-dot" :class="rec.status" />
          <span class="rec-title">{{ rec.title }}</span>
          <span class="rec-time">{{ rec.time }}</span>
          <span v-if="rec.duration !== undefined" class="rec-dur">
            {{ rec.duration }}ms
          </span>
          <span class="rec-status-tag" :class="rec.status">
            {{ rec.status === 'success' ? '成功' : rec.status === 'error' ? '失败' : '请求中' }}
          </span>
          <span class="toggle-icon">{{ expanded[idx] ? '▲' : '▼' }}</span>
        </div>

        <!-- 卡片内容 -->
        <div v-show="expanded[idx]" class="record-body">
          <!-- URL -->
          <div class="section">
            <div class="section-label">
              <span class="method-badge">POST</span>
              <span class="url-text">{{ rec.url }}</span>
            </div>
          </div>

          <!-- 请求参数 -->
          <div class="section">
            <div class="section-title">📤 请求参数</div>
            <pre class="code-block req">{{ formatJson(rec.reqBody) }}</pre>
          </div>

          <!-- 响应数据 -->
          <div class="section">
            <div class="section-title">
              📥 响应数据
              <span v-if="rec.status === 'pending'" class="loading-dot">...</span>
            </div>
            <pre
              class="code-block"
              :class="rec.status === 'error' ? 'err' : 'res'"
            >{{ rec.resBody !== undefined ? formatJson(rec.resBody) : '等待响应...' }}</pre>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.api-detail {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  font-size: 13px;
  font-weight: 700;
  color: #90caf9;
  border-bottom: 1px solid rgba(100, 181, 246, 0.1);
  background: linear-gradient(90deg, rgba(13, 71, 161, 0.3), transparent);
  flex-shrink: 0;
}

.empty-state {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: #37474f;
}

.records-wrap {
  flex: 1;
  overflow-y: auto !important;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

/* 记录卡片 */
.record-card {
  border-radius: 8px;
  border: 1px solid rgba(100, 181, 246, 0.1);
  background: #0d1117;
  margin-bottom: 2px;
}

.record-card.success {
  border-color: rgba(0, 200, 83, 0.2);
}

.record-card.error {
  border-color: rgba(244, 67, 54, 0.25);
}

/* 标题行 */
.record-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 14px;
  cursor: pointer;
  background: rgba(255, 255, 255, 0.02);
  transition: background 0.15s;
  user-select: none;
}

.record-header:hover {
  background: rgba(100, 181, 246, 0.05);
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-dot.pending { background: #ffab40; animation: pulse 1s infinite; }
.status-dot.success { background: #00e676; }
.status-dot.error   { background: #ff5252; }

.rec-title {
  flex: 1;
  font-size: 12px;
  font-weight: 600;
  color: #cfd8dc;
}

.rec-time {
  font-size: 10px;
  color: #455a64;
}

.rec-dur {
  font-size: 10px;
  color: #42a5f5;
  background: rgba(66, 165, 245, 0.1);
  padding: 1px 6px;
  border-radius: 10px;
}

.rec-status-tag {
  font-size: 10px;
  padding: 1px 8px;
  border-radius: 10px;
  font-weight: 600;
}

.rec-status-tag.success {
  background: rgba(0, 200, 83, 0.12);
  color: #00e676;
}

.rec-status-tag.error {
  background: rgba(244, 67, 54, 0.12);
  color: #ff5252;
}

.rec-status-tag.pending {
  background: rgba(255, 171, 64, 0.12);
  color: #ffab40;
}

.toggle-icon {
  font-size: 10px;
  color: #546e7a;
}

/* 内容区 */
.record-body {
  padding: 0 14px 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.section-label {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.method-badge {
  background: rgba(21, 101, 192, 0.3);
  color: #64b5f6;
  font-size: 10px;
  font-weight: 700;
  padding: 2px 7px;
  border-radius: 3px;
  flex-shrink: 0;
}

.url-text {
  font-size: 11px;
  color: #78909c;
  font-family: 'Consolas', monospace;
  word-break: break-all;
}

.section-title {
  font-size: 11px;
  font-weight: 600;
  color: #546e7a;
  display: flex;
  align-items: center;
  gap: 6px;
}

.loading-dot {
  color: #ffab40;
  animation: pulse 1s infinite;
}

.code-block {
  background: #060a10;
  border-radius: 6px;
  padding: 12px;
  font-family: 'Consolas', monospace;
  font-size: 12px;
  line-height: 1.5;
  overflow: auto !important;
  max-height: 400px;
  white-space: pre;
  margin: 0;
  border: 1px solid rgba(100, 181, 246, 0.1);
  color: #eceff1;
}

.code-block.req  { color: #80cbc4; }
.code-block.res  { color: #a5d6a7; }
.code-block.err  { color: #ef9a9a; }

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>

